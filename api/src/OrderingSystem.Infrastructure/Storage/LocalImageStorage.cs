using Microsoft.Extensions.Options;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Exceptions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace OrderingSystem.Infrastructure.Storage;

/// <summary>
/// Stores menu images on local disk, re-encoded to WebP.
/// <para>
/// Three things happen before anything is written, and each closes a real hole:
/// the byte count is capped <em>before</em> decoding, because decoding an enormous or
/// deliberately malformed file is the denial-of-service; the content is decoded rather than
/// trusted by extension, because a <c>.png</c> that is really a script is the oldest upload bug
/// there is; and the result is re-encoded rather than stored as received, which discards
/// everything in the original that was not pixels — EXIF, trailing archives, embedded payloads.
/// </para>
/// </summary>
public sealed class LocalImageStorage(IOptions<ImageStorageOptions> options) : IImageStorage
{
    private readonly ImageStorageOptions _options = options.Value;

    public async Task<string> SaveAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        await CopyWithLimitAsync(content, buffer, _options.MaxBytes, cancellationToken);
        buffer.Position = 0;

        Image image;
        try
        {
            image = await Image.LoadAsync(buffer, cancellationToken);
        }
        catch (UnknownImageFormatException)
        {
            throw new ValidationFailedException(
                "That file is not an image.",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["file"] = ["Upload a PNG, JPEG or WebP image."],
                });
        }
        catch (InvalidImageContentException)
        {
            throw new ValidationFailedException(
                "That image could not be read.",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["file"] = ["The file looks like an image but its contents are damaged."],
                });
        }

        using (image)
        {
            if (image.Width > _options.MaxDimension || image.Height > _options.MaxDimension)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(_options.MaxDimension, _options.MaxDimension),
                }));
            }

            // Metadata is dropped rather than carried forward. A restaurant photo taken on a phone
            // arrives with the GPS coordinates of the kitchen in it.
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;

            var fileName = $"{Guid.NewGuid():N}.webp";
            var directory = Path.GetFullPath(_options.RootPath);
            Directory.CreateDirectory(directory);

            await using var file = File.Create(Path.Combine(directory, fileName));
            await image.SaveAsync(file, new WebpEncoder { Quality = _options.Quality }, cancellationToken);

            return $"{_options.PublicPath.TrimEnd('/')}/{fileName}";
        }
    }

    public Task DeleteAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        var fileName = Path.GetFileName(url);

        // Only ever a bare file name from our own directory. Without this, a stored value of
        // "../../appsettings.json" would delete whatever the process can reach.
        if (string.IsNullOrEmpty(fileName) || fileName.Contains("..", StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        var path = Path.Combine(Path.GetFullPath(_options.RootPath), fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Copies at most <paramref name="limit"/> bytes and throws if the source has more. Reading
    /// Length is not enough: a chunked upload does not have one.
    /// </summary>
    private static async Task CopyWithLimitAsync(
        Stream source, Stream destination, long limit, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > limit)
            {
                throw new ValidationFailedException(
                    "That image is too large.",
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["file"] = [$"Images must be under {limit / (1024 * 1024)} MB."],
                    });
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}
