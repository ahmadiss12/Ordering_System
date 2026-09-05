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

// ---------------------------------------------------------------- your own account

/// <param name="MustSetPassword">
/// True for somebody invited who has not chosen a password yet. On screen it is the difference
/// between "change your password" and "you have not set one".
/// </param>
public sealed record ProfileResponse(
    Guid Id,
    string Email,
    string FullName,
    string Phone,
    bool MustSetPassword,
    IReadOnlyList<string> Roles);

/// <summary>
/// What a person may correct about themselves.
/// </summary>
/// <remarks>
/// Not the email address. It is the login identifier and the only way back into a locked-out
/// account, so changing it needs the new address proved before the old one stops working — a
/// verification flow, not a text box. Until that exists, offering the box would let somebody
/// mistype themselves out of their own account and their order history with it.
/// </remarks>
public sealed record UpdateProfileRequest(string FullName, string Phone);

/// <param name="CurrentPassword">
/// Required even though the caller is signed in. A borrowed phone or a stolen token is a session;
/// letting it set a new password without proving the old one turns that into the account itself,
/// permanently and silently.
/// </param>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
