namespace OrderingSystem.Application.Abstractions;

/// <summary>
/// Hashing lives behind an interface so the application never names a specific algorithm. When
/// today's choice is superseded, one implementation changes and nothing else does.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Constant-time comparison. Never compare hashes with string equality.</summary>
    bool Verify(string hash, string password);
}
