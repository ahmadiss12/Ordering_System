using OrderingSystem.Application.Abstractions;

namespace OrderingSystem.Infrastructure.Time;

/// <summary>The real clock. Tests substitute their own so expiry and thresholds are testable.</summary>
public sealed class SystemClock : IClock
{
    // Order numbering and opening hours are local-calendar concepts. Hard-coded because the
    // platform serves one country; it becomes a per-restaurant column the day that stops being true.
    private static readonly TimeZoneInfo Beirut = ResolveBeirut();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset LocalNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Beirut);

    public DateOnly LocalToday => DateOnly.FromDateTime(LocalNow.DateTime);

    private static TimeZoneInfo ResolveBeirut()
    {
        // .NET understands IANA ids on Windows too, but fall back rather than crash on startup
        // if the host's timezone database is unusual.
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Beirut", TimeSpan.FromHours(2), "Beirut", "Beirut");
        }
    }
}
