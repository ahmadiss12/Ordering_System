namespace OrderingSystem.Application.Abstractions;

/// <summary>
/// Where uploaded images live, behind an interface because they will not always live in the same
/// place. Local disk is fine for development and wrong for production: files do not survive a
/// container restart and cannot be shared between servers. Swapping to object storage should be a
/// new implementation, not a rewrite of everything that uploads.
/// </summary>
public interface IImageStorage
{
    /// <summary>
    /// Validates, normalises and stores an image, returning the URL to serve it from.
    /// <para>
    /// Implementations must re-encode rather than store what arrived. A file that is a valid
    /// image <em>and</em> something else is a real attack, and re-encoding discards everything
    /// that is not pixels.
    /// </para>
    /// </summary>
    Task<string> SaveAsync(Stream content, CancellationToken cancellationToken = default);

    /// <summary>Removes a previously stored image. Missing is not an error.</summary>
    Task DeleteAsync(string url, CancellationToken cancellationToken = default);
}
