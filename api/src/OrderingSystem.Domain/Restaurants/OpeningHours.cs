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
