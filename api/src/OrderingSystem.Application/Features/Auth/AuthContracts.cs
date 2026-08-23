namespace OrderingSystem.Application.Features.Auth;

public sealed record RegisterRequest(string Email, string Password, string FullName, string Phone);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

/// <summary>
/// What every successful auth call returns. The refresh token is the only time its plaintext
/// exists outside the client — the database keeps a hash.
/// </summary>
public sealed record AuthTokensResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
