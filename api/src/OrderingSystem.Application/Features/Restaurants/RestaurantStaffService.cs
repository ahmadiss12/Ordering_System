using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Application.Features.Auth;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Identity;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Application.Features.Restaurants;

/// <summary>
/// Who works at a restaurant, and what they may do there.
///
/// <para>
/// The most dangerous screen in the phase. A <see cref="RestaurantStaff"/> row is what puts the
/// restaurant_id claim in somebody's token, so adding one hands over every order, every customer
/// address and every phone number the restaurant holds. Nothing here takes a restaurant id from
/// the caller: it comes from the token, through <see cref="ITenantGuard.RequireRestaurantId"/>.
/// </para>
/// <para>
/// Two role systems have to move together and that is the trap in this file. A person's global
/// <see cref="RoleType"/> decides which endpoints they can reach, and their per-restaurant
/// <see cref="StaffRoleType"/> decides what they are here. Set one without the other and you get
/// an owner who cannot open the owner screens, or worse, a demoted owner who still can. Every
/// write below goes through <see cref="ApplyRoleAsync"/> so the pairing exists in one place.
/// </para>
/// </summary>
public sealed class RestaurantStaffService(
    IAppDbContext db,
    ITenantGuard guard,
    ITenantContext tenant,
    IValidationService validation,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender,
    IClock clock,
    IOptions<AuthOptions> options)
{
    private readonly AuthOptions _options = options.Value;

    public async Task<IReadOnlyList<StaffMemberResponse>> ListAsync(CancellationToken ct = default)
    {
        var restaurantId = guard.RequireRestaurantId();

        return await db.RestaurantStaff.AsNoTracking()
            .Where(s => s.RestaurantId == restaurantId)
            // Owners first, then by name: the list is read to find somebody, and "who can approve
            // this" is the question it is usually opened for.
            .OrderByDescending(s => s.StaffRole)
            .ThenBy(s => s.User.FullName)
            .Select(s => new StaffMemberResponse(
                s.UserId,
                s.User.Email,
                s.User.FullName,
                s.StaffRole,
                s.User.MustSetPassword,
                s.UserId == tenant.UserId,
                s.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Adds somebody to the staff list, creating their account if they do not have one.
    ///
    /// <para>
    /// An address that already has an account is reused rather than duplicated. That matters more
    /// than it looks: the person a restaurant hires is very often already a customer here, and a
    /// second account would strand their order history on an address they can no longer sign in
    /// with. It is also why the caller is told whether an invitation was actually emailed —
    /// an existing colleague signs in with the password they already have, and a screen that
    /// claimed to have sent them a link would be lying.
    /// </para>
    /// </summary>
    public async Task<StaffMemberResponse> InviteAsync(
        InviteStaffRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        var restaurantId = guard.RequireRestaurantId();
        var email = Normalize(request.Email);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is { IsActive: false })
        {
            throw new ConflictException(
                "That account has been deactivated. The platform has to restore it before they can be added.");
        }

        if (user is not null)
        {
            await EnsureNotAlreadyStaffAsync(user.Id, restaurantId, ct);
        }
        else
        {
            user = CreateInvitedAccount(email, request);
            db.Users.Add(user);
            db.UserRoles.Add(new UserRole { UserId = user.Id, Role = RoleType.Customer });
        }

        db.RestaurantStaff.Add(new RestaurantStaff
        {
            UserId = user.Id,
            RestaurantId = restaurantId,
            StaffRole = request.StaffRole,
            CreatedAt = clock.UtcNow,
        });

        await ApplyRoleAsync(user.Id, request.StaffRole, ct);
        await db.SaveChangesAsync(ct);

        await SendInvitationAsync(user, restaurantId, ct);

        return await SingleAsync(user.Id, ct);
    }

    /// <summary>
    /// Promotes or demotes somebody, yourself included.
    ///
    /// <para>
    /// Your own account on purpose. An owner handing over promotes their successor and then steps
    /// back, and needing a second person to perform the second half of that would be a strange
    /// thing to insist on. The last-owner rule is what stops it going wrong, and it is a better
    /// guard than a ban on self-service: a rule against acting on yourself would leave the last
    /// owner unreachable by anybody at all, which is the same protection by accident and no
    /// protection once a second owner exists.
    /// </para>
    /// </summary>
    public async Task<StaffMemberResponse> SetRoleAsync(
        Guid userId, SetStaffRoleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        var restaurantId = guard.RequireRestaurantId();
        var member = await LoadMemberAsync(userId, restaurantId, ct);

        if (member.StaffRole == StaffRoleType.Owner && request.StaffRole != StaffRoleType.Owner)
        {
            await RefuseIfLastOwnerAsync(restaurantId, "demote", ct);
        }

        member.StaffRole = request.StaffRole;
        await ApplyRoleAsync(userId, request.StaffRole, ct);
        await db.SaveChangesAsync(ct);

        return await SingleAsync(userId, ct);
    }

    /// <summary>
    /// Takes somebody off the staff list, yourself included — an owner is allowed to resign.
    /// The account survives either way: they may still be a customer, and their orders have to
    /// keep resolving whoever placed them.
    /// </summary>
    public async Task RemoveAsync(Guid userId, CancellationToken ct = default)
    {
        var restaurantId = guard.RequireRestaurantId();
        var member = await LoadMemberAsync(userId, restaurantId, ct);

        if (member.StaffRole == StaffRoleType.Owner)
        {
            await RefuseIfLastOwnerAsync(restaurantId, "remove", ct);
        }

        db.RestaurantStaff.Remove(member);
        await ApplyRoleAsync(userId, null, ct);

        // Ending their sessions is the point of the whole operation. A refresh token issued while
        // they worked here stays valid for a month otherwise, and although refreshing re-reads
        // the staff table and would no longer find them, leaving a dismissed employee holding a
        // live session is not a thing to be relaxed about. Their unexpired access token still
        // carries the claim until it lapses, which is the honest limit of what this can do.
        var live = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in live)
        {
            token.RevokedAt = clock.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ rules

    /// <summary>
    /// Refuses an invitation the token could not honour.
    ///
    /// <para>
    /// A person may hold rows at two restaurants — the key is (UserId, RestaurantId) — but an
    /// access token carries exactly one restaurant_id, and nothing lets its holder say which. So
    /// a second row would decide their tenant by whichever the database returned first. Refusing
    /// is the honest answer until there is a way to switch between them.
    /// </para>
    /// </summary>
    private async Task EnsureNotAlreadyStaffAsync(Guid userId, Guid restaurantId, CancellationToken ct)
    {
        // IgnoreQueryFilters deliberately: the filter narrows RestaurantStaff to the caller's own
        // restaurant, and the row being looked for is by definition at somebody else's. Only the
        // fact that one exists is used - no other restaurant's data is read or returned.
        var elsewhere = await db.RestaurantStaff
            .IgnoreQueryFilters()
            .AnyAsync(s => s.UserId == userId && s.RestaurantId != restaurantId, ct);

        if (elsewhere)
        {
            throw new ConflictException(
                "That person already works at another restaurant on the platform and cannot be added to a second.");
        }

        var here = await db.RestaurantStaff
            .AnyAsync(s => s.UserId == userId && s.RestaurantId == restaurantId, ct);

        if (here)
        {
            throw new ConflictException("They are already on your staff list.");
        }
    }

    /// <summary>
    /// The last-owner rule, and the only thing standing between a restaurant and needing platform
    /// support to recover from one click. With no owner there is nobody who can set a fee, edit
    /// the hours, or invite anybody — including anybody who could put an owner back.
    /// </summary>
    private async Task RefuseIfLastOwnerAsync(Guid restaurantId, string verb, CancellationToken ct)
    {
        var owners = await db.RestaurantStaff
            .CountAsync(s => s.RestaurantId == restaurantId && s.StaffRole == StaffRoleType.Owner, ct);

        if (owners <= 1)
        {
            throw new ConflictException(
                $"You cannot {verb} the last owner. Make somebody else an owner first.");
        }
    }

    /// <summary>
    /// Keeps the global role in step with the restaurant role. Null means they have left, and
    /// takes both restaurant roles away without touching Customer or PlatformAdmin — leaving here
    /// is not a reason to stop being able to order dinner.
    /// </summary>
    private async Task ApplyRoleAsync(Guid userId, StaffRoleType? staffRole, CancellationToken ct)
    {
        var wanted = staffRole switch
        {
            StaffRoleType.Owner => RoleType.RestaurantOwner,
            StaffRoleType.Staff => RoleType.RestaurantStaff,
            _ => (RoleType?)null,
        };

        var held = await db.UserRoles
            .Where(r => r.UserId == userId
                && (r.Role == RoleType.RestaurantOwner || r.Role == RoleType.RestaurantStaff))
            .ToListAsync(ct);

        foreach (var role in held.Where(r => r.Role != wanted))
        {
            db.UserRoles.Remove(role);
        }

        if (wanted is { } keep && !held.Exists(r => r.Role == keep))
        {
            db.UserRoles.Add(new UserRole { UserId = userId, Role = keep });
        }
    }

    // ------------------------------------------------------------------ invitations

    /// <summary>
    /// A brand-new account with no way into it. The password hash is real — a hash of a discarded
    /// random secret — rather than a sentinel, so <c>Verify</c> behaves normally and simply never
    /// matches. Nobody, including whoever invited them, knows a password for this account.
    /// </summary>
    private User CreateInvitedAccount(string email, InviteStaffRequest request) =>
        new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = passwordHasher.Hash(TokenHashing.NewToken()),
            FullName = request.FullName.Trim(),
            Phone = request.Phone?.Trim() ?? string.Empty,
            IsActive = true,
            MustSetPassword = true,
            CreatedAt = clock.UtcNow,
        };

    private async Task SendInvitationAsync(User user, Guid restaurantId, CancellationToken ct)
    {
        var restaurantName = await db.Restaurants.AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => r.Name)
            .FirstAsync(ct);

        if (!user.MustSetPassword)
        {
            // They already had an account, so there is no link to send and nothing to set up.
            // Their next sign-in picks up the restaurant, because the token is built from the
            // staff table each time one is issued.
            await emailSender.SendAsync(
                user.Email,
                $"You have been added to {restaurantName}",
                $"""
                 Hello {user.FullName},

                 {restaurantName} has added you to their staff on Ordering System. Sign in with
                 the password you already use and the restaurant's screens will be there.

                 If you were not expecting this, reply to this message and tell us.
                 """,
                ct);
            return;
        }

        var token = TokenHashing.NewToken();
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = TokenHashing.Hash(token),
            CreatedAt = clock.UtcNow,
            // Days, where a password reset gets hours. Somebody recovering their own account is
            // sitting at the screen waiting for the mail; somebody being hired may not read it
            // until their next shift.
            ExpiresAt = clock.UtcNow.AddDays(_options.InvitationDays),
        });
        await db.SaveChangesAsync(ct);

        var link = $"{_options.AppBaseUrl.TrimEnd('/')}/reset-password" +
            $"?token={Uri.EscapeDataString(token)}&invited=1";

        await emailSender.SendAsync(
            user.Email,
            $"{restaurantName} has invited you to Ordering System",
            $"""
             Hello {user.FullName},

             {restaurantName} would like you to help run their orders. Choose a password using the
             link below and you can sign in. It works once, and expires in {_options.InvitationDays} days.

             {link}

             If you have no idea what this is, ignore this message - no account of yours has been
             changed.
             """,
            ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<RestaurantStaff> LoadMemberAsync(Guid userId, Guid restaurantId, CancellationToken ct) =>
        await db.RestaurantStaff.FirstOrDefaultAsync(
            s => s.UserId == userId && s.RestaurantId == restaurantId, ct)
        ?? throw new NotFoundException("That person is not on your staff list.");

    private async Task<StaffMemberResponse> SingleAsync(Guid userId, CancellationToken ct) =>
        (await ListAsync(ct)).FirstOrDefault(m => m.UserId == userId)
        ?? throw new NotFoundException("That person is not on your staff list.");

    /// <summary>Lowercased and trimmed, exactly as registration and login do it.</summary>
    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
