namespace OrderingSystem.Domain.Identity;

/// <summary>
/// One issued refresh token. Rotating and single-use: refreshing marks this row used and issues
/// a successor in the same family. Presenting a token that already has <see cref="UsedAt"/> set
/// means it leaked, and the whole family is revoked — see ADR-10.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>
    /// Every token descended from one login shares this. Revoking on reuse works on the family,
    /// not the single row, so a stolen token cannot be traded for a fresh one.
    /// </summary>
    public Guid FamilyId { get; set; }

    /// <summary>
    /// SHA-256 of the token, never the token itself. A leaked database must not hand out
    /// working sessions.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when exchanged. Non-null here plus a fresh presentation is the theft signal.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>Set on logout, on password change, or when the family is revoked.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public User User { get; set; } = null!;
}
