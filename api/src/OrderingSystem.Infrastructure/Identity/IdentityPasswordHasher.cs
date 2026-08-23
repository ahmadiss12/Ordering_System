using Microsoft.AspNetCore.Identity;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Identity;

namespace OrderingSystem.Infrastructure.Identity;

/// <summary>
/// Wraps ASP.NET Core Identity's password hasher — PBKDF2 with a per-password salt and a current
/// iteration count, maintained by Microsoft.
/// <para>
/// We take this one piece and leave the rest of Identity alone: its user, role and claim schema
/// would fight the model in the spec, particularly staff membership scoped to a restaurant.
/// </para>
/// </summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private static readonly User Dummy = new();
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(Dummy, password);

    public bool Verify(string hash, string password) =>
        _inner.VerifyHashedPassword(Dummy, hash, password) is
            PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
}
