namespace OrderingSystem.Domain.Restaurants;

/// <summary>
/// Decides whether a restaurant is open at a given local moment.
/// <para>
/// Two shapes make this less trivial than it looks. A kitchen may close between lunch and dinner,
/// so one day can hold several windows. And a kitchen may run past midnight, which is stored as a
/// close time earlier than the open time — meaning an order at 01:00 on Tuesday belongs to
/// Monday's window, not to Tuesday's.
/// </para>
/// </summary>
public static class OpeningHours
{
    /// <summary>Minutes in a day and in a week, for laying windows out on one timeline.</summary>
    private const int MinutesInDay = 24 * 60;
    private const int MinutesInWeek = 7 * MinutesInDay;

    /// <summary>
    /// The first pair of windows that cover the same moment, or null when none do.
    ///
    /// <para>
    /// Overlapping windows are harmless to <see cref="IsOpenAt"/> — it returns true if any window
    /// matches — which is exactly why they are worth refusing at the point somebody types them.
    /// A kitchen that enters 12:00–16:00 and 14:00–20:00 meant 19:00, and nothing downstream will
    /// ever tell them otherwise.
    /// </para>
    /// <para>
    /// Laid out on a single weekly timeline rather than compared day by day, because a window that
    /// crosses midnight belongs to two days: Monday 18:00–02:00 and Tuesday 01:00–05:00 both cover
    /// Tuesday at half past one, and a same-day comparison would never see it.
    /// </para>
    /// </summary>
    public static (RestaurantHours First, RestaurantHours Second)? FindOverlap(
        IEnumerable<RestaurantHours> hours)
    {
        ArgumentNullException.ThrowIfNull(hours);

        var spans = hours
            .Select(h => (Window: h, Span: SpanOf(h)))
            .ToArray();

        for (var i = 0; i < spans.Length; i++)
        {
            for (var j = i + 1; j < spans.Length; j++)
            {
                if (Intersects(spans[i].Span, spans[j].Span))
                {
                    return (spans[i].Window, spans[j].Window);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Where a window sits on the week, in minutes from Monday midnight. A window that closes
    /// before it opens runs past the end of its day, so its end simply reaches beyond its start —
    /// and one that runs past Sunday night wraps, which is what the second span below is for.
    /// </summary>
    private static (int Start, int End) SpanOf(RestaurantHours window)
    {
        var start = (DayIndex(window.DayOfWeek) * MinutesInDay) + Minutes(window.OpenTime);
        var length = window.CloseTime > window.OpenTime
            ? Minutes(window.CloseTime) - Minutes(window.OpenTime)
            : MinutesInDay - Minutes(window.OpenTime) + Minutes(window.CloseTime);

        return (start, start + length);
    }

    /// <summary>
    /// Whether two spans cover a common minute, allowing for a window that runs past Sunday into
    /// Monday. Touching at the boundary is not an overlap: 12:00–16:00 and 16:00–20:00 are two
    /// sittings, which is a normal way to describe a day.
    /// </summary>
    private static bool Intersects((int Start, int End) a, (int Start, int End) b) =>
        Wrapped(a).Any(x => Wrapped(b).Any(y => x.Start < y.End && y.Start < x.End));

    /// <summary>
    /// A span as one or two pieces: anything running past the end of the week reappears at the
    /// start of it, because Sunday night's late window and Monday morning are the same minutes.
    /// </summary>
    private static IEnumerable<(int Start, int End)> Wrapped((int Start, int End) span)
    {
        if (span.End <= MinutesInWeek)
        {
            yield return span;
            yield break;
        }

        yield return (span.Start, MinutesInWeek);
        yield return (0, span.End - MinutesInWeek);
    }

    /// <summary>Monday first, because a week of opening hours is read starting there.</summary>
    private static int DayIndex(DayOfWeek day) => ((int)day + 6) % 7;

    private static int Minutes(TimeOnly time) => (time.Hour * 60) + time.Minute;

    public static bool IsOpenAt(
        IEnumerable<RestaurantHours> hours, DayOfWeek day, TimeOnly localTime)
    {
        ArgumentNullException.ThrowIfNull(hours);

        var windows = hours as IReadOnlyCollection<RestaurantHours> ?? [.. hours];

        foreach (var window in windows.Where(h => h.DayOfWeek == day))
        {
            // Ordinary window: opens and closes on the same day.
            if (window.CloseTime > window.OpenTime)
            {
                if (localTime >= window.OpenTime && localTime < window.CloseTime)
                {
                    return true;
                }
            }

            // Overnight window, before midnight. 12:00-02:00 is open at 23:00 on the day it
            // opens, and stays open until the close time on the following day - which the
            // previous-day pass below handles. Missing this half is the easy bug: the kitchen
            // looks shut all evening.
            else if (window.CloseTime < window.OpenTime && localTime >= window.OpenTime)
            {
                return true;
            }

            // CloseTime == OpenTime is a zero-length window and reads as closed, deliberately.
            // A kitchen that never closes is written TimeOnly.MinValue to TimeOnly.MaxValue —
            // not 00:00-23:59, which is shut for the last sixty seconds of every day and is the
            // sort of thing that fails one automated run in fourteen hundred and forty.
        }

        // A window opened yesterday that has not closed yet. FriesLab's 12:00–02:00 is stored on
        // the day it opens, so at 01:00 the row to look at belongs to the previous day.
        var yesterday = day == DayOfWeek.Sunday ? DayOfWeek.Saturday : day - 1;

        foreach (var window in windows.Where(h => h.DayOfWeek == yesterday))
        {
            if (window.CloseTime < window.OpenTime && localTime < window.CloseTime)
            {
                return true;
            }
        }

        return false;
    }
}
