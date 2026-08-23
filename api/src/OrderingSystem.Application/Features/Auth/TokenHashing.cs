using System.Security.Cryptography;
using System.Text;

namespace OrderingSystem.Application.Features.Auth;

/// <summary>
/// Refresh and reset tokens are stored hashed, never in plaintext. Someone who reads the database
/// gets values they cannot present back to the API.
/// <para>
/// Plain SHA-256 rather than a slow password hash is right here: these tokens are 256 bits of
/// cryptographic randomness, so there is nothing to brute-force and no salt to add. A slow hash
/// would only make every refresh slower.
/// </para>
/// </summary>
internal static class TokenHashing
{
    public static string NewToken()
    {
        // 32 bytes from a cryptographic RNG. URL-safe so it survives being put in a link.
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
