using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;
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
    IClock clock,
    StaffInvitations invitations)
{

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
    public async Task<InvitedStaffResponse> InviteAsync(
        InviteStaffRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        var restaurantId = guard.RequireRestaurantId();

        var (userId, emailed) = await invitations.InviteAsync(
            restaurantId, request.Email, request.FullName, request.Phone, request.StaffRole, ct: ct);

        return new InvitedStaffResponse(await SingleAsync(userId, ct), emailed);
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
        await invitations.ApplyRoleAsync(userId, request.StaffRole, ct);
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
        await invitations.ApplyRoleAsync(userId, null, ct);

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
