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
    public void A_restaurant_with_no_hours_is_closed()
    {
        OpeningHours.IsOpenAt([], DayOfWeek.Monday, new TimeOnly(12, 0)).ShouldBeFalse();
    }

    private static RestaurantHours Window(DayOfWeek day, int openHour, int openMinute, int closeHour, int closeMinute) =>
        new()
        {
            DayOfWeek = day,
            OpenTime = new TimeOnly(openHour, openMinute),
            CloseTime = new TimeOnly(closeHour, closeMinute),
        };
}
