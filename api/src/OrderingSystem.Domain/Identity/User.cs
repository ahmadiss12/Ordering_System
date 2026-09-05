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

    /// <summary>
    /// True for an account created by a staff invitation, until its owner follows the emailed link
    /// and chooses a password.
    ///
    /// <para>
    /// It exists so a restaurant owner can tell "invited, has not signed in yet" from "working
    /// here", which nothing else in the model answers: an invited account is created with a hash
    /// of a discarded secret, and an unusable password looks exactly like a normal one from
    /// outside. Somebody invited who already had an account here is never marked — they have a
    /// password already.
    /// </para>
    /// </summary>
    public bool MustSetPassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<UserRole> Roles { get; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; } = [];
    public ICollection<PasswordResetToken> PasswordResetTokens { get; } = [];
    public ICollection<RestaurantStaff> StaffMemberships { get; } = [];
    public ICollection<Address> Addresses { get; } = [];
    public ICollection<Cart> Carts { get; } = [];
}
