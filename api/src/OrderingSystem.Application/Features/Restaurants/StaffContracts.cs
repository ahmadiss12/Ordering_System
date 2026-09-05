using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Application.Features.Restaurants;

/// <summary>
/// One person on a restaurant's staff list.
/// </summary>
/// <param name="MustSetPassword">
/// True while an invitation is outstanding. The screen shows it as "invited" rather than as a
/// working colleague, because somebody who has not set a password cannot sign in yet.
/// </param>
/// <param name="IsYou">
/// So the screen can stop an owner removing themselves by accident. The server does not rely on
/// it — it recomputes who is calling from the token.
/// </param>
public sealed record StaffMemberResponse(
    Guid UserId,
    string Email,
    string FullName,
    StaffRoleType StaffRole,
    bool MustSetPassword,
    bool IsYou,
    DateTimeOffset CreatedAt);

/// <param name="Email">
/// Matched against existing accounts. Somebody who already orders here is added to the staff list
/// rather than given a second account, so their order history follows them.
/// </param>
/// <param name="FullName">
/// Used only when the invitation creates an account. An existing account keeps the name its owner
/// chose — a restaurant does not get to rename a customer.
/// </param>
public sealed record InviteStaffRequest(
    string Email,
    string FullName,
    string? Phone,
    StaffRoleType StaffRole);

public sealed record SetStaffRoleRequest(StaffRoleType StaffRole);
