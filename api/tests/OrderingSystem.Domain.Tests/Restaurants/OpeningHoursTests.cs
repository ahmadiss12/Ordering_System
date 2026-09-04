using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Domain.Tests.Restaurants;

public class OpeningHoursTests
{
    [Theory]
    [InlineData(11, 59, false, "a minute before opening")]
    [InlineData(12, 00, true, "exactly at opening")]
    [InlineData(18, 30, true, "mid-service")]
    [InlineData(22, 59, true, "a minute before closing")]
    [InlineData(23, 00, false, "closing time itself is closed")]
    public void A_normal_day_opens_and_closes_as_written(int hour, int minute, bool expected, string why)
    {
        var hours = new[] { Window(DayOfWeek.Monday, 12, 0, 23, 0) };

        OpeningHours.IsOpenAt(hours, DayOfWeek.Monday, new TimeOnly(hour, minute))
            .ShouldBe(expected, why);
    }

    [Theory]
    [InlineData(13, 00, true, "lunch service")]
    [InlineData(17, 00, false, "the gap between services")]
    [InlineData(20, 00, true, "dinner service")]
    public void A_kitchen_that_closes_between_services_has_a_gap(int hour, int minute, bool expected, string why)
    {
        // The reason RestaurantHours allows several rows per day.
        var hours = new[]
        {
            Window(DayOfWeek.Monday, 12, 0, 16, 0),
            Window(DayOfWeek.Monday, 19, 0, 23, 30),
        };

        OpeningHours.IsOpenAt(hours, DayOfWeek.Monday, new TimeOnly(hour, minute))
            .ShouldBe(expected, why);
    }

    [Fact]
    public void A_window_running_past_midnight_is_still_open_after_midnight()
    {
        // Monday 12:00 to 02:00. At 01:00 on Tuesday the kitchen is open, and the row that says
        // so belongs to Monday.
        var hours = new[] { Window(DayOfWeek.Monday, 12, 0, 2, 0) };

        OpeningHours.IsOpenAt(hours, DayOfWeek.Tuesday, new TimeOnly(1, 0)).ShouldBeTrue();
        OpeningHours.IsOpenAt(hours, DayOfWeek.Tuesday, new TimeOnly(3, 0)).ShouldBeFalse();
        OpeningHours.IsOpenAt(hours, DayOfWeek.Monday, new TimeOnly(23, 0)).ShouldBeTrue();
    }

    [Fact]
    public void Sunday_night_spills_into_monday_morning()
    {
        // The wrap-around case: "yesterday" from Sunday is Saturday, and from Monday is Sunday.
        var hours = new[] { Window(DayOfWeek.Sunday, 18, 0, 2, 0) };

        OpeningHours.IsOpenAt(hours, DayOfWeek.Monday, new TimeOnly(1, 0)).ShouldBeTrue();
        OpeningHours.IsOpenAt(hours, DayOfWeek.Saturday, new TimeOnly(1, 0)).ShouldBeFalse();
    }

    [Fact]
    public void A_kitchen_that_never_closes_is_open_at_every_minute_of_the_day()
    {
        // MinValue to MaxValue, not 00:00-23:59. The obvious spelling is shut for the last sixty
        // seconds of every day, which is how an end-to-end suite fails one run in fourteen
        // hundred and forty and nobody can reproduce it.
        var hours = new[]
        {
            new RestaurantHours
            {
                DayOfWeek = DayOfWeek.Monday,
                OpenTime = TimeOnly.MinValue,
                CloseTime = TimeOnly.MaxValue,
            },
        };

        foreach (var minute in new[] { new TimeOnly(0, 0), new TimeOnly(12, 0), new TimeOnly(23, 59) })
        {
            OpeningHours.IsOpenAt(hours, DayOfWeek.Monday, minute)
                .ShouldBeTrue($"a kitchen that never closes is open at {minute}");
        }
    }

    [Fact]
    public void The_obvious_way_to_write_all_day_leaves_a_minute_shut()
    {
        // Not a rule anybody wants, but it is the rule: `localTime < CloseTime` is exclusive.
        // Asserting it is what stops somebody "tidying" the seed back to 00:00-23:59.
        var hours = new[] { Window(DayOfWeek.Monday, 0, 0, 23, 59) };

        OpeningHours.IsOpenAt(hours, DayOfWeek.Monday, new TimeOnly(23, 58)).ShouldBeTrue();
        OpeningHours.IsOpenAt(hours, DayOfWeek.Monday, new TimeOnly(23, 59)).ShouldBeFalse();
    }

    [Fact]
    public void A_restaurant_with_no_hours_is_closed()
    {
        OpeningHours.IsOpenAt([], DayOfWeek.Monday, new TimeOnly(12, 0)).ShouldBeFalse();
    }

    // ------------------------------------------------------------------ overlapping windows

    [Fact]
    public void Two_sittings_on_one_day_do_not_overlap()
    {
        // The mezze house's shape: lunch, a gap, dinner. The commonest legitimate schedule there
        // is, so it had better not be refused.
        var hours = new[]
        {
            Window(DayOfWeek.Monday, 12, 0, 16, 0),
            Window(DayOfWeek.Monday, 19, 0, 23, 30),
        };

        OpeningHours.FindOverlap(hours).ShouldBeNull();
    }

    [Fact]
    public void Sittings_that_touch_are_not_an_overlap()
    {
        // Noon to four and four to eight is a normal way to describe a day, and the minute at
        // four o'clock belongs to exactly one of them.
        var hours = new[]
        {
            Window(DayOfWeek.Monday, 12, 0, 16, 0),
            Window(DayOfWeek.Monday, 16, 0, 20, 0),
        };

        OpeningHours.FindOverlap(hours).ShouldBeNull();
    }

    [Fact]
    public void A_window_swallowing_another_is_an_overlap()
    {
        // Somebody meant nineteen hundred. Nothing downstream would ever tell them: IsOpenAt
        // returns true if any window matches, so the mistake is invisible in behaviour.
        var hours = new[]
        {
            Window(DayOfWeek.Monday, 12, 0, 16, 0),
            Window(DayOfWeek.Monday, 14, 0, 20, 0),
        };

        OpeningHours.FindOverlap(hours).ShouldNotBeNull();
    }

    [Fact]
    public void A_late_window_clashing_with_the_next_mornings_is_an_overlap()
    {
        // The case a day-by-day comparison cannot see. Monday's window runs to two in the
        // morning; Tuesday's starts at one, and half past one is covered twice.
        var hours = new[]
        {
            Window(DayOfWeek.Monday, 18, 0, 2, 0),
            Window(DayOfWeek.Tuesday, 1, 0, 5, 0),
        };

        OpeningHours.FindOverlap(hours).ShouldNotBeNull();
    }

    [Fact]
    public void A_late_window_ending_before_the_next_one_starts_is_fine()
    {
        var hours = new[]
        {
            Window(DayOfWeek.Monday, 18, 0, 2, 0),
            Window(DayOfWeek.Tuesday, 12, 0, 16, 0),
        };

        OpeningHours.FindOverlap(hours).ShouldBeNull();
    }

    [Fact]
    public void Sunday_night_clashing_with_monday_morning_is_an_overlap()
    {
        // The wrap-around again, and the one an implementation is most likely to miss: Sunday's
        // late window runs past the end of the week and lands back at the start of it.
        var hours = new[]
        {
            Window(DayOfWeek.Sunday, 20, 0, 3, 0),
            Window(DayOfWeek.Monday, 2, 0, 6, 0),
        };

        OpeningHours.FindOverlap(hours).ShouldNotBeNull();
    }

    [Fact]
    public void A_week_of_identical_days_never_overlaps_itself()
    {
        // The seed's own shape, and what an editor produces when somebody copies Monday to every
        // day. Refusing this would make the commonest action on the screen impossible.
        var hours = Enum.GetValues<DayOfWeek>().Select(d => Window(d, 10, 0, 23, 0)).ToArray();

        OpeningHours.FindOverlap(hours).ShouldBeNull();
    }

    [Fact]
    public void A_week_of_identical_overnight_days_never_overlaps_itself()
    {
        // FriesLab's shape, copied across the week: each day runs to two in the morning and the
        // next opens at noon, so nothing ever meets.
        var hours = Enum.GetValues<DayOfWeek>().Select(d => Window(d, 12, 0, 2, 0)).ToArray();

        OpeningHours.FindOverlap(hours).ShouldBeNull();
    }

    [Fact]
    public void Nothing_at_all_overlaps_nothing()
    {
        OpeningHours.FindOverlap([]).ShouldBeNull();
    }

    private static RestaurantHours Window(DayOfWeek day, int openHour, int openMinute, int closeHour, int closeMinute) =>
        new()
        {
            DayOfWeek = day,
            OpenTime = new TimeOnly(openHour, openMinute),
            CloseTime = new TimeOnly(closeHour, closeMinute),
        };
}
