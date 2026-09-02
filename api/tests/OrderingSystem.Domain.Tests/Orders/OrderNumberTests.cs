using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Domain.Tests.Orders;

/// <summary>The reference a customer quotes to support and a kitchen calls across a counter.</summary>
public class OrderNumberTests
{
    private static readonly DateOnly Day = new(2026, 9, 2);

    [Fact]
    public void Reads_as_restaurant_day_and_number()
    {
        OrderNumbers.Format("frieslab", Day, 42).ShouldBe("FRIESLAB-260902-042");
    }

    [Fact]
    public void The_days_first_order_is_number_one()
    {
        OrderNumbers.Format("frieslab", Day, 1).ShouldBe("FRIESLAB-260902-001");
    }

    [Fact]
    public void Hyphens_in_a_slug_do_not_become_extra_sections()
    {
        // Otherwise the reference reads as four parts and nobody can tell where the name ends.
        var number = OrderNumbers.Format("beirut-mezze-house", Day, 7);

        number.ShouldBe("BEIRUTMEZZ-260902-007");
        number.Split('-').Length.ShouldBe(3);
    }

    [Fact]
    public void A_busy_day_grows_the_counter_rather_than_wrapping()
    {
        OrderNumbers.Format("frieslab", Day, 1234).ShouldBe("FRIESLAB-260902-1234");
    }

    [Fact]
    public void Every_plausible_reference_fits_the_column()
    {
        // nvarchar(32). A longer value would be refused by SQL Server at the worst possible
        // moment — after the money has been worked out and the basket is about to be emptied.
        foreach (var slug in new[] { "a", "frieslab", "beirut-mezze-house", new string('x', 120) })
        {
            foreach (var sequence in new[] { 1, 999, 100_000 })
            {
                OrderNumbers.Format(slug, Day, sequence).Length
                    .ShouldBeLessThanOrEqualTo(OrderNumbers.MaxLength, $"slug '{slug[..Math.Min(12, slug.Length)]}'");
            }
        }
    }

    [Fact]
    public void Two_restaurants_with_different_names_get_different_references()
    {
        var a = OrderNumbers.Format("frieslab", Day, 1);
        var b = OrderNumbers.Format("shawarma-station", Day, 1);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void The_same_restaurant_never_repeats_a_number_within_a_day()
    {
        var numbers = Enumerable.Range(1, 500)
            .Select(i => OrderNumbers.Format("frieslab", Day, i))
            .ToHashSet();

        numbers.Count.ShouldBe(500);
    }

    [Fact]
    public void The_counter_resets_but_the_date_keeps_the_reference_unique()
    {
        var today = OrderNumbers.Format("frieslab", Day, 1);
        var tomorrow = OrderNumbers.Format("frieslab", Day.AddDays(1), 1);

        today.ShouldNotBe(tomorrow);
    }

    [Fact]
    public void A_sequence_below_one_is_a_programming_error()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => OrderNumbers.Format("frieslab", Day, 0));
    }

    [Fact]
    public void A_missing_slug_is_a_programming_error()
    {
        Should.Throw<ArgumentException>(() => OrderNumbers.Format("  ", Day, 1));
    }
}
