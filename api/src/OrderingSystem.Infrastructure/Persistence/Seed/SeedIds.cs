using System.Security.Cryptography;
using System.Text;

namespace OrderingSystem.Infrastructure.Persistence.Seed;

/// <summary>
/// Turns a name into a stable GUID, so seeding twice produces the same ids.
/// <para>
/// Random ids would mean every re-seed invalidates bookmarks, saved Postman requests and any
/// screenshot of the dashboard. A demo you have to re-learn after each reset is a worse demo.
/// </para>
/// </summary>
internal static class SeedIds
{
    /// <summary>Same input, same GUID, on every machine and every run.</summary>
    public static Guid From(string name)
    {
        // SHA-256 rather than MD5: nothing here is security-sensitive, but a broken hash in
        // the codebase invites the wrong conclusion from anyone reading it later. The first
        // 16 bytes are all a GUID needs.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ordering-system-seed:" + name));
        return new Guid(hash.AsSpan(0, 16));
    }
}
