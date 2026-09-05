using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Application.Features.Auth;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Identity;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Application.Features.Restaurants;

/// <summary>
/// Putting a person on a restaurant's staff: reusing or creating their account, giving them the
/// membership and the matching global role, and telling them.
///
/// <para>
/// <b>This class authorises nothing.</b> It takes the restaurant id it is told, and hands whoever
/// is named the keys to that restaurant's entire order book. Two services call it and each does
/// its own check first — an owner inviting a colleague goes through
/// <see cref="ITenantGuard.RequireRestaurantId"/>, and the platform appointing a brand-new
/// restaurant's first owner goes through <see cref="ITenantGuard.RequirePlatformAdmin"/>. There is
/// no third caller, and a third one would have to answer the same question before it got here.
/// </para>
/// <para>
/// It exists because the two paths are the same act. The first owner of a restaurant that has no
/// staff yet cannot be invited by a member of its staff, so the platform has to do it — and
/// copying this logic to do so would have left two versions of "is this address already an
/// account", two versions of the unusable password, and two invitation emails to keep in step.
/// </para>
/// </summary>
public sealed partial class StaffInvitations(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender,
    IClock clock,
    ILogger<StaffInvitations> logger,
    IOptions<AuthOptions> options)
{
    private readonly AuthOptions _options = options.Value;

    /// <param name="allowExistingMembership">
    /// False everywhere. A restaurant being created has no staff at all, so the checks for
    /// "already works here" and "already works somewhere else" apply to it exactly as they do to
    /// an ordinary invitation — the second especially, since a token carries one restaurant.
    /// </param>
    public async Task<(Guid UserId, bool Emailed)> InviteAsync(
        Guid restaurantId,
        string email,
        string fullName,
        string? phone,
        StaffRoleType staffRole,
        bool allowExistingMembership = false,
        CancellationToken ct = default)
    {
        var normalised = Normalize(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalised, ct);

        if (user is { IsActive: false })
        {
            throw new ConflictException(
                "That account has been deactivated. The platform has to restore it before they can be added.");
        }

        if (user is not null && !allowExistingMembership)
        {
            await EnsureNotAlreadyStaffAsync(user.Id, restaurantId, ct);
        }
        else if (user is null)
        {
            user = CreateInvitedAccount(normalised, fullName, phone);
            db.Users.Add(user);
            db.UserRoles.Add(new UserRole { UserId = user.Id, Role = RoleType.Customer });
        }

        db.RestaurantStaff.Add(new RestaurantStaff
        {
            UserId = user.Id,
            RestaurantId = restaurantId,
            StaffRole = staffRole,
            CreatedAt = clock.UtcNow,
        });

        await ApplyRoleAsync(user.Id, staffRole, ct);
        await db.SaveChangesAsync(ct);

        return (user.Id, await SendInvitationAsync(user, restaurantId, ct));
    }

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
    public async Task EnsureNotAlreadyStaffAsync(Guid userId, Guid restaurantId, CancellationToken ct)
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
            .IgnoreQueryFilters()
            .AnyAsync(s => s.UserId == userId && s.RestaurantId == restaurantId, ct);

        if (here)
        {
            throw new ConflictException("They are already on your staff list.");
        }
    }

    /// <summary>
    /// Keeps the global role in step with the restaurant role. Null means they have left, and
    /// takes both restaurant roles away without touching Customer or PlatformAdmin — leaving here
    /// is not a reason to stop being able to order dinner.
    /// </summary>
    public async Task ApplyRoleAsync(Guid userId, StaffRoleType? staffRole, CancellationToken ct)
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

    /// <summary>
    /// A brand-new account with no way into it. The password hash is real — a hash of a discarded
    /// random secret — rather than a sentinel, so <c>Verify</c> behaves normally and simply never
    /// matches. Nobody, including whoever invited them, knows a password for this account.
    /// </summary>
    private User CreateInvitedAccount(string email, string fullName, string? phone) =>
        new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = passwordHasher.Hash(TokenHashing.NewToken()),
            FullName = fullName.Trim(),
            Phone = phone?.Trim() ?? string.Empty,
            IsActive = true,
            MustSetPassword = true,
            CreatedAt = clock.UtcNow,
        };

    /// <summary>
    /// Tells them, and reports whether that worked.
    ///
    /// <para>
    /// Nothing in here is allowed to throw. By the time it runs the staff row is committed, so an
    /// exception would surface to the caller as "something went wrong" about an operation that in
    /// fact succeeded — and the person would be on the list the next time they looked. A mail
    /// server that is briefly down is an ordinary event, not a reason to appear to fail.
    /// </para>
    /// </summary>
    private async Task<bool> SendInvitationAsync(User user, Guid restaurantId, CancellationToken ct)
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
            await TrySendAsync(
                user.Email,
                $"You have been added to {restaurantName}",
                $"""
                 Hello {user.FullName},

                 {restaurantName} has added you to their staff on Ordering System. Sign in with
                 the password you already use and the restaurant's screens will be there.

                 If you were not expecting this, reply to this message and tell us.
                 """);

            // Nothing was sent that anybody is waiting for. They sign in with the password they
            // already have, so a notice that did not arrive costs them nothing.
            return false;
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

        return await TrySendAsync(
            user.Email,
            $"{restaurantName} has invited you to Ordering System",
            $"""
             Hello {user.FullName},

             {restaurantName} would like you to help run their orders. Choose a password using the
             link below and you can sign in. It works once, and expires in {_options.InvitationDays} days.

             {link}

             If you have no idea what this is, ignore this message - no account of yours has been
             changed.
             """);
    }

    private async Task<bool> TrySendAsync(string to, string subject, string body)
    {
        try
        {
            // Deliberately not passing the request's cancellation token. The caller has already
            // been committed to; abandoning the mail because they closed the tab would leave
            // somebody on a staff list with no way to sign in and nothing saying so.
            await emailSender.SendAsync(to, subject, body, CancellationToken.None);
            return true;
        }
        catch (Exception exception)
        {
            // Broad on purpose. Every mail library has its own exception type and its own set of
            // transient network failures underneath, and none of them is worth failing a
            // completed operation over. The token is in the database either way, so an owner who
            // removes and re-invites gets a fresh link.
            LogInvitationNotSent(logger, exception, to);
            return false;
        }
    }

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Error,
        Message = "Could not email a staff invitation to {Email}. They are on the staff list; "
            + "removing and re-inviting them issues a fresh link.")]
    private static partial void LogInvitationNotSent(ILogger logger, Exception exception, string email);

    /// <summary>Lowercased and trimmed, exactly as registration and login do it.</summary>
    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
