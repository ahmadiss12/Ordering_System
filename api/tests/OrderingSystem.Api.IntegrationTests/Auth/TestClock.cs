using OrderingSystem.Application.Abstractions;

namespace OrderingSystem.Api.IntegrationTests.Auth;

/// <summary>
/// A clock whose local time is pinned, so that tests about opening hours do not depend on the
/// hour they happen to run at.
///
/// <para>
/// The problem it solves is real: FriesLab is seeded open from noon until two in the morning, so
/// every test that places an order failed for the ten hours a day the restaurant is shut. That is
/// the checkout behaving correctly and the tests being wrong to depend on wall-clock time.
/// </para>
/// <para>
/// <see cref="UtcNow"/> deliberately stays real while <see cref="LocalNow"/> is pinned. Tokens
/// are issued against UtcNow and validated by the framework's own clock, which no test can move,
/// so a UTC time hours away from the real one would make every request arrive with an expired
/// token. Only opening hours and the order-number business date read LocalNow, and both want a
/// value that does not wander.
/// </para>
/// </summary>
public sealed class TestClock : IClock
{
    private static readonly TimeZoneInfo Beirut = ResolveBeirut();

    /// <summary>
    /// One in the afternoon: inside every seeded restaurant's hours, including the mezze house
    /// which shuts between lunch and dinner.
    /// </summary>
    public static readonly TimeOnly DefaultLocalTime = new(13, 0);

    /// <summary>Set to move the restaurant's wall clock — to prove the closed refusal, say.</summary>
    public TimeOnly LocalTimeOfDay { get; set; } = DefaultLocalTime;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset LocalNow
    {
        get
        {
            // Today's date in Beirut, at whatever time of day the test asked for. Keeping the
            // date real means anything the seeder wrote with the real clock — the exchange rate,
            // thirty days back — is still in the past.
            var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Beirut);

            return new DateTimeOffset(
                DateOnly.FromDateTime(today.Date),
                LocalTimeOfDay,
                today.Offset);
        }
    }

    public DateOnly LocalToday => DateOnly.FromDateTime(LocalNow.DateTime);

    private static TimeZoneInfo ResolveBeirut()
    {
        foreach (var id in new[] { "Asia/Beirut", "Middle East Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the other naming scheme.
            }
        }

        return TimeZoneInfo.Utc;
    }
}
