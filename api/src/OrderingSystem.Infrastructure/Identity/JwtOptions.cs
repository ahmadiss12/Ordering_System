namespace OrderingSystem.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "ordering-system";
    public string Audience { get; set; } = "ordering-system-clients";

    /// <summary>
    /// Never committed. Supplied per environment through configuration; the API refuses to start
    /// without one rather than falling back to a default that would end up in production.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Short by design. Revocation happens at refresh time, so a long access token is a long window.</summary>
    public int AccessTokenMinutes { get; set; } = 15;
}
