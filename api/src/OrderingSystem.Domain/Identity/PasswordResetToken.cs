namespace OrderingSystem.Domain.Identity;

/// <summary>
/// Single-use, short-lived, and hashed for the same reason refresh tokens are: the row must not
/// be usable by anyone who reads the database.
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }

    public User User { get; set; } = null!;
}
