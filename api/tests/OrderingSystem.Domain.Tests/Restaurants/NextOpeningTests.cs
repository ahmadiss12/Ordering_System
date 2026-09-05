using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Domain.Tests.Restaurants;

/// <summary>
/// When a shut kitchen opens again.
///
/// <para>
/// A card that says only "Closed" makes somebody browsing at ten in the morning guess which of
/// five shut restaurants is worth waiting for. These pin the awkward corners: the week wrapping
/// round, a day with two sittings, and the difference between "later today" and "tomorrow".
/// </para>
/// </summary>
public class NextOpeningTests
{
    [Fact]
    public void Finds_the_next_window_later_the_same_day()
    {
        var hours = new[] { Window(DayOfWeek.Monday, "18:00", "23:00") };

        var next = OpeningHours.NextOpeningAfter(hours, DayOfWeek.Monday, new TimeOnly(10, 0));

        next.ShouldNotBeNull();
        next.Value.Time.ShouldBe(new TimeOnly(18, 0));
        next.Value.DaysAway.ShouldBe(0);
    }

    [Fact]
    public void Picks_the_earlier_of_two_sittings_on_one_day()
    {
        var hours = new[]
        {
            Window(DayOfWeek.Monday, "18:00", "23:00"),
            Window(DayOfWeek.Monday, "12:00", "16:00"),
        };

        // A kitchen that shuts between lunch and dinner. Somebody looking at nine wants lunch.
        var next = OpeningHours.NextOpeningAfter(hours, DayOfWeek.Monday, new TimeOnly(9, 0));

        next!.Value.Time.ShouldBe(new TimeOnly(12, 0));
    }

    [Fact]
    public void Moves_to_the_next_sitting_once_the_first_has_opened()
    {
        var hours = new[]
        {
            Window(DayOfWeek.Monday, "12:00", "16:00"),
            Window(DayOfWeek.Monday, "18:00", "23:00"),
        };

        // Half past four, between the two. Lunch has been and gone.
        var next = OpeningHours.NextOpeningAfter(hours, DayOfWeek.Monday, new TimeOnly(16, 30));

        next!.Value.Time.ShouldBe(new TimeOnly(18, 0));
        next.Value.DaysAway.ShouldBe(0);
    }

    [Fact]
    public void Counts_tomorrow_as_a_day_away_however_late_it_is_tonight()
    {
        var hours = new[] { Window(DayOfWeek.Tuesday, "09:00", "17:00") };

        var lateTonight = OpeningHours.NextOpeningAfter(hours, DayOfWeek.Monday, new TimeOnly(23, 0));
        var thisMorning = OpeningHours.NextOpeningAfter(hours, DayOfWeek.Monday, new TimeOnly(8, 0));

        // Ten hours and twenty-five hours, and both are "tomorrow". Rounding the wait down would
        // call the first one "later today", which is a lie told to somebody at eleven at night.
        lateTonight!.Value.DaysAway.ShouldBe(1);
        thisMorning!.Value.DaysAway.ShouldBe(1);
    }

    [Fact]
    public void Wraps_round_the_end_of_the_week()
    {
        var hours = new[] { Window(DayOfWeek.Monday, "09:00", "17:00") };

        var next = OpeningHours.NextOpeningAfter(hours, DayOfWeek.Sunday, new TimeOnly(20, 0));

        // Sunday evening looking at a kitchen that only opens on Mondays. Comparing day numbers
        // would find nothing after Sunday and give up.
        next.ShouldNotBeNull();
        next.Value.Day.ShouldBe(DayOfWeek.Monday);
        next.Value.DaysAway.ShouldBe(1);
    }

    [Fact]
    public void Says_a_week_away_for_a_kitchen_that_opens_one_day_only()
    {
        var hours = new[] { Window(DayOfWeek.Friday, "18:00", "23:00") };

        var next = OpeningHours.NextOpeningAfter(hours, DayOfWeek.Friday, new TimeOnly(23, 30));

        // Just missed it. The next Friday is the answer, not "no answer".
        next!.Value.Day.ShouldBe(DayOfWeek.Friday);
        next.Value.DaysAway.ShouldBe(0, "seven days round the week is the same weekday again");
    }

    [Fact]
    public void Has_no_answer_for_a_kitchen_with_no_hours_at_all()
    {
        // Closed indefinitely, which the product allows on purpose. A card can say "Closed" and
        // nothing more, which is the truth.
        OpeningHours.NextOpeningAfter([], DayOfWeek.Monday, new TimeOnly(10, 0)).ShouldBeNull();
    }

    [Fact]
    public void Answers_even_while_the_kitchen_is_open()
    {
        var hours = new[] { Window(DayOfWeek.Monday, "09:00", "17:00") };

        // Not what a card asks for, but a defined answer beats an accidental one: the next time
        // it opens is next Monday, not "now".
        var next = OpeningHours.NextOpeningAfter(hours, DayOfWeek.Monday, new TimeOnly(12, 0));

        next!.Value.Time.ShouldBe(new TimeOnly(9, 0));
    }

    private static RestaurantHours Window(DayOfWeek day, string open, string close) => new()
    {
        DayOfWeek = day,
        OpenTime = TimeOnly.Parse(open, System.Globalization.CultureInfo.InvariantCulture),
        CloseTime = TimeOnly.Parse(close, System.Globalization.CultureInfo.InvariantCulture),
    };
}
