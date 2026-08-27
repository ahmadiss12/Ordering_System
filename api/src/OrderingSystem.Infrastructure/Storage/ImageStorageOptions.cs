namespace OrderingSystem.Infrastructure.Storage;

public sealed class ImageStorageOptions
{
    public const string SectionName = "ImageStorage";

    /// <summary>Directory images are written to. Created on startup if missing.</summary>
    public string RootPath { get; set; } = "wwwroot/media";

    /// <summary>URL prefix the stored files are served under.</summary>
    public string PublicPath { get; set; } = "/media";

    /// <summary>Rejected before anything is decoded — decoding an enormous file is the attack.</summary>
    public long MaxBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Longest edge after resizing. A menu photo does not need to be 6000px wide.</summary>
    public int MaxDimension { get; set; } = 1600;

    /// <summary>WebP quality. 80 is visually indistinguishable from source for photographs.</summary>
    public int Quality { get; set; } = 80;
}
