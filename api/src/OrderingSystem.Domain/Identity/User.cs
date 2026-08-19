using OrderingSystem.Domain.Carts;
using OrderingSystem.Domain.Geography;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Domain.Identity;

/// <summary>
/// One account. Roles are additive and live in <see cref="UserRole"/>, so the same person can be
/// a restaurant owner and still place orders as a customer.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Unique, and the login identifier. Stored lowercased so lookups are unambiguous.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>PBKDF2 via IPasswordHasher. Never a plaintext or reversible value.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    /// <summary>Deactivation instead of deletion — orders must keep resolving their customer.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<UserRole> Roles { get; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; } = [];
    public ICollection<PasswordResetToken> PasswordResetTokens { get; } = [];
    public ICollection<RestaurantStaff> StaffMemberships { get; } = [];
    public ICollection<Address> Addresses { get; } = [];
    public ICollection<Cart> Carts { get; } = [];
}
