namespace OrderingSystem.Application.Features.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>How long a refresh token stays valid if never used.</summary>
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>Password reset links are deliberately short-lived.</summary>
    public int PasswordResetHours { get; set; } = 2;

    /// <summary>Front-end origin the reset link points at.</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:4200";
}
