using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Identity;

namespace OrderingSystem.Application.Features.Auth;

/// <summary>
/// Registration, login, refresh-token rotation and password recovery.
/// <para>
/// Three behaviours here are security decisions rather than conveniences, and each is commented
/// where it happens: login does not reveal whether an address is registered, forgotten-password
/// answers identically for known and unknown addresses, and re-presenting a spent refresh token
/// revokes every session descended from that login.
/// </para>
/// </summary>
public sealed class AuthService(
    IAppDbContext db,
    IValidationService validation,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer tokenIssuer,
    IEmailSender emailSender,
    IClock clock,
    IOptions<AuthOptions> options)
{
    private readonly AuthOptions _options = options.Value;

    // ------------------------------------------------------------------ register

    public async Task<AuthTokensResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        var email = Normalize(request.Email);

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new ConflictException("An account with that email already exists.");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = request.FullName.Trim(),
            Phone = request.Phone.Trim(),
            IsActive = true,
            CreatedAt = clock.UtcNow,
        };
        user.PasswordHash = passwordHasher.Hash(request.Password);

        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, Role = RoleType.Customer });
        await db.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, familyId: Guid.NewGuid(), ct);
    }

    // ------------------------------------------------------------------ login

    public async Task<AuthTokensResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        var email = Normalize(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            // Hash anyway so a missing account takes about as long as a wrong password. Without
            // this, response time alone tells an attacker which addresses are registered.
            _ = passwordHasher.Hash(request.Password);
            throw new AuthenticationFailedException("Invalid email or password.");
        }

        // Deliberately the same message as above: which half was wrong is not the caller's business.
        if (!passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            throw new AuthenticationFailedException("Invalid email or password.");
        }

        if (!user.IsActive)
        {
            throw new ForbiddenException("This account has been deactivated.");
        }

        return await IssueTokensAsync(user, familyId: Guid.NewGuid(), ct);
    }

    // ------------------------------------------------------------------ refresh

    public async Task<AuthTokensResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        var hash = TokenHashing.Hash(request.RefreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
        {
            throw new AuthenticationFailedException("Invalid refresh token.");
        }

        // Already spent. The legitimate client rotated away from this token, so whoever is
        // presenting it now got it some other way - revoke the entire family rather than just
        // refusing, because the thief may already hold a newer one.
        if (stored.UsedAt is not null)
        {
            await RevokeFamilyAsync(stored.FamilyId, ct);
            throw new AuthenticationFailedException(
                "This refresh token has already been used. All sessions have been signed out.");
        }

        if (stored.RevokedAt is not null || stored.ExpiresAt <= clock.UtcNow)
        {
            throw new AuthenticationFailedException("Refresh token is no longer valid.");
        }

        if (!stored.User.IsActive)
        {
            throw new ForbiddenException("This account has been deactivated.");
        }

        stored.UsedAt = clock.UtcNow;
        return await IssueTokensAsync(stored.User, stored.FamilyId, ct);
    }

    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        var hash = TokenHashing.Hash(request.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        // Signing out with a token we do not recognise is not an error worth reporting - the
        // caller wanted to be signed out, and they are.
        if (stored is not null)
        {
            await RevokeFamilyAsync(stored.FamilyId, ct);
        }
    }

    // ------------------------------------------------------------------ password recovery

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        var email = Normalize(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive, ct);

        // No account, no email - but the caller is told the same thing either way. An endpoint
        // that answers differently for known addresses is a membership oracle.
        if (user is null)
        {
            return;
        }

        var token = TokenHashing.NewToken();
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = TokenHashing.Hash(token),
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddHours(_options.PasswordResetHours),
        });
        await db.SaveChangesAsync(ct);

        var link = $"{_options.AppBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";
        await emailSender.SendAsync(
            user.Email,
            "Reset your password",
            $"""
             Hello {user.FullName},

             Use the link below to choose a new password. It expires in {_options.PasswordResetHours} hours
             and can be used once.

             {link}

             If you did not ask for this, you can ignore this message - your password has not changed.
             """,
            ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        var hash = TokenHashing.Hash(request.Token);
        var token = await db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null || token.UsedAt is not null || token.ExpiresAt <= clock.UtcNow)
        {
            throw new AuthenticationFailedException("This reset link is invalid or has expired.");
        }

        token.UsedAt = clock.UtcNow;
        token.User.PasswordHash = passwordHasher.Hash(request.NewPassword);

        // Choosing a password is what accepting a staff invitation consists of, so this is where
        // the invitation stops being outstanding. Harmless for an ordinary reset: the flag is
        // already false for everyone who was not invited.
        token.User.MustSetPassword = false;

        // Changing a password ends every existing session. If the reset happened because the
        // account was compromised, leaving the attacker's refresh tokens alive defeats the point.
        var live = await db.RefreshTokens
            .Where(t => t.UserId == token.UserId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var refresh in live)
        {
            refresh.RevokedAt = clock.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<AuthTokensResponse> IssueTokensAsync(User user, Guid familyId, CancellationToken ct)
    {
        var roles = await db.UserRoles
            .Where(r => r.UserId == user.Id)
            .Select(r => r.Role)
            .ToListAsync(ct);

        // IgnoreQueryFilters is required, not a shortcut: RestaurantStaff is tenant-filtered, and
        // this is the query that decides what the tenant IS. Reading it through the filter would
        // ask a question that can only be answered after it has been answered.
        // Ordered, because FirstOrDefault on an unordered query is whatever SQL Server hands back
        // first. Invitations refuse to create a second membership for exactly this reason, but
        // the composite key still permits one, and a token whose tenant changed between logins
        // would be a miserable thing to diagnose. Oldest wins - the job they held first.
        var restaurantId = await db.RestaurantStaff
            .IgnoreQueryFilters()
            .Where(s => s.UserId == user.Id)
            .OrderBy(s => s.CreatedAt)
            .ThenBy(s => s.RestaurantId)
            .Select(s => (Guid?)s.RestaurantId)
            .FirstOrDefaultAsync(ct);

        var (accessToken, accessExpiry) = tokenIssuer.Issue(user, roles, restaurantId);

        var refreshToken = TokenHashing.NewToken();
        var refreshExpiry = clock.UtcNow.AddDays(_options.RefreshTokenDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FamilyId = familyId,
            TokenHash = TokenHashing.Hash(refreshToken),
            CreatedAt = clock.UtcNow,
            ExpiresAt = refreshExpiry,
        });
        await db.SaveChangesAsync(ct);

        return new AuthTokensResponse(accessToken, accessExpiry, refreshToken, refreshExpiry);
    }

    private async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var family = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in family)
        {
            token.RevokedAt = clock.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Lowercased and trimmed, so "Ahmad@X.com " and "ahmad@x.com" are one account.</summary>
    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
