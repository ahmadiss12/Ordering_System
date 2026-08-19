namespace OrderingSystem.Infrastructure.Persistence;

/// <summary>
/// Column sizes and numeric precisions, named once. Without an explicit length EF Core maps a
/// string to nvarchar(max), which cannot be indexed and is stored off-row — so every string column
/// in this schema gets a size from here.
/// </summary>
internal static class Lengths
{
    public const int Email = 256;
    public const int PasswordHash = 256;

    /// <summary>SHA-256 rendered as hex is exactly 64 characters.</summary>
    public const int TokenHash = 64;

    public const int PersonName = 200;
    public const int Phone = 32;
    public const int RestaurantName = 200;
    public const int Slug = 120;
    public const int Url = 500;
    public const int Description = 1000;
    public const int EntityName = 200;
    public const int AddressLine = 300;
    public const int AddressPart = 100;
    public const int Note = 500;
    public const int OrderNumber = 32;
    public const int ProviderRef = 200;
    public const int SettingKey = 100;
    public const int SettingValue = 500;
}

internal static class Precision
{
    /// <summary>All money. decimal, never float — see the money rules in ARCHITECTURE.md.</summary>
    public const int MoneyPrecision = 10;
    public const int MoneyScale = 2;

    /// <summary>0.00 to 100.00.</summary>
    public const int PercentPrecision = 5;
    public const int PercentScale = 2;

    /// <summary>About 11cm of resolution, which is far finer than a Beirut landmark needs.</summary>
    public const int CoordinatePrecision = 9;
    public const int CoordinateScale = 6;

    /// <summary>LBP per USD runs to five figures and moves often, so it gets room.</summary>
    public const int ExchangeRatePrecision = 18;
    public const int ExchangeRateScale = 4;
}
