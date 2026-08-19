using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Domain.Identity;

/// <summary>
/// Composite key (UserId, Role) — a user cannot hold the same role twice, and the database
/// enforces that rather than the application remembering to check.
/// </summary>
public class UserRole
{
    public Guid UserId { get; set; }
    public RoleType Role { get; set; }

    public User User { get; set; } = null!;
}
