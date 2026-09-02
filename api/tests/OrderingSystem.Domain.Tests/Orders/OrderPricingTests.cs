using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Domain.Tests.Orders;

/// <summary>
/// The money. Every figure an order stores comes from here, so a receipt and a settlement report
/// cannot disagree by each doing their own arithmetic.
/// </summary>
public class OrderPricingTests
{
    private static PricingInputs Inputs(
        IReadOnlyCollection<PricedLine>? lines = null,
        decimal deliveryFee = 0m,
        decimal discount = 0m,
        decimal commissionPercent = 0m,
        int prepMinutes = 0,
        int travelMinutes = 0,
        decimal minOrder = 0m,
        decimal? rate = null) =>
        new(lines ?? [], deliveryFee, discount, commissionPercent, prepMinutes, travelMinutes, minOrder, rate);

    [Fact]
    public void A_subtotal_is_the_lines_added_up()
    {
        var price = OrderPricing.Calculate(Inputs([
            new PricedLine(7.50m, 2),
            new PricedLine(9.75m, 1),
        ]));

        price.SubtotalUsd.ShouldBe(24.75m);
        price.TotalUsd.ShouldBe(24.75m);
    }

    [Fact]
    public void An_empty_basket_costs_nothing_rather_than_failing()
    {
        var price = OrderPricing.Calculate(Inputs());

        price.SubtotalUsd.ShouldBe(0m);
        price.TotalUsd.ShouldBe(0m);
        price.CommissionUsd.ShouldBe(0m);
    }

    [Fact]
    public void Delivery_and_discount_move_the_total_but_not_the_subtotal()
    {
        var price = OrderPricing.Calculate(Inputs(
            [new PricedLine(20.00m, 1)], deliveryFee: 3.50m, discount: 5.00m));

        price.SubtotalUsd.ShouldBe(20.00m);
        price.DeliveryFeeUsd.ShouldBe(3.50m);
        price.DiscountUsd.ShouldBe(5.00m);
        price.TotalUsd.ShouldBe(18.50m);
    }

    [Fact]
    public void Tax_is_zero_and_still_reported()
    {
        // Named rather than dropped, so the day VAT applies is a configuration change.
        var price = OrderPricing.Calculate(Inputs([new PricedLine(10m, 1)]));

        price.TaxUsd.ShouldBe(0m);
    }

    [Fact]
    public void The_lines_always_add_up_to_the_subtotal_exactly()
    {
        // The thing a customer checks by hand. Decimal is what makes it hold; with double,
        // 0.10 x 3 is not 0.30.
        var lines = new[]
        {
            new PricedLine(0.10m, 3),
            new PricedLine(0.20m, 3),
            new PricedLine(3.33m, 3),
        };

        var price = OrderPricing.Calculate(Inputs(lines));

        price.SubtotalUsd.ShouldBe(lines.Sum(l => l.UnitPriceUsd * l.Quantity));
        price.SubtotalUsd.ShouldBe(10.89m);
    }

    // ------------------------------------------------------------------ commission

    [Fact]
    public void Commission_is_taken_on_food_and_not_on_the_delivery_fee()
    {
        // Charging commission on the fee would bill the restaurant for the courier.
        var price = OrderPricing.Calculate(Inputs(
            [new PricedLine(100.00m, 1)], deliveryFee: 10.00m, commissionPercent: 15m));

        price.CommissionUsd.ShouldBe(15.00m);
        price.TotalUsd.ShouldBe(110.00m);
    }

    [Fact]
    public void Commission_rounds_to_the_cent_away_from_zero()
    {
        // 10.05 x 15% = 1.5075, which no column can hold. Banker's rounding would give 1.50 here
        // and 1.52 on the next order up, which is right for statistics and wrong on an invoice.
        var price = OrderPricing.Calculate(Inputs(
            [new PricedLine(10.05m, 1)], commissionPercent: 15m));

        price.CommissionUsd.ShouldBe(1.51m);
    }

    [Fact]
    public void A_zero_commission_restaurant_owes_nothing()
    {
        var price = OrderPricing.Calculate(Inputs([new PricedLine(50m, 1)], commissionPercent: 0m));

        price.CommissionUsd.ShouldBe(0m);
    }

    [Fact]
    public void The_percentage_in_force_is_reported_back()
    {
        // Copied onto the order so that changing a restaurant's rate never restates past
        // settlement.
        var price = OrderPricing.Calculate(Inputs([new PricedLine(10m, 1)], commissionPercent: 12.5m));

        price.CommissionPercent.ShouldBe(12.5m);
    }

    // ------------------------------------------------------------------ the promise

    [Fact]
    public void A_delivery_promise_is_prep_plus_travel()
    {
        var price = OrderPricing.Calculate(Inputs(prepMinutes: 20, travelMinutes: 15));

        price.PromisedMinutesMin.ShouldBe(35);
        price.PromisedMinutesMax.ShouldBe(45);
    }

    [Fact]
    public void A_pickup_promise_has_no_travel_in_it()
    {
        var price = OrderPricing.Calculate(Inputs(prepMinutes: 20, travelMinutes: 0));

        price.PromisedMinutesMin.ShouldBe(20);
        price.PromisedMinutesMax.ShouldBe(30);
    }

    [Fact]
    public void The_promise_is_a_window_and_never_a_single_minute()
    {
        // A promise to the minute is one no kitchen keeps and every customer judges it by.
        var price = OrderPricing.Calculate(Inputs(prepMinutes: 25, travelMinutes: 10));

        price.PromisedMinutesMax.ShouldBeGreaterThan(price.PromisedMinutesMin);
    }

    // ------------------------------------------------------------------ minimum order

    [Fact]
    public void The_minimum_is_measured_against_food_not_the_total()
    {
        // A delivery fee carrying somebody over the minimum would make the minimum meaningless.
        var price = OrderPricing.Calculate(Inputs(
            [new PricedLine(6.00m, 1)], deliveryFee: 3.00m, minOrder: 8.00m));

        price.MeetsMinimum.ShouldBeFalse();
        price.ShortfallUsd.ShouldBe(2.00m);
    }

    [Fact]
    public void Exactly_the_minimum_is_enough()
    {
        var price = OrderPricing.Calculate(Inputs([new PricedLine(8.00m, 1)], minOrder: 8.00m));

        price.MeetsMinimum.ShouldBeTrue();
        price.ShortfallUsd.ShouldBe(0m);
    }

    [Fact]
    public void A_basket_over_the_minimum_reports_no_shortfall()
    {
        // Never a negative number: the screen says "you need $2 more", and there is no sensible
        // reading of "you need minus three dollars more".
        var price = OrderPricing.Calculate(Inputs([new PricedLine(20m, 1)], minOrder: 8m));

        price.MeetsMinimum.ShouldBeTrue();
        price.ShortfallUsd.ShouldBe(0m);
    }

    // ------------------------------------------------------------------ lebanese pounds

    [Fact]
    public void Pounds_are_whole_numbers()
    {
        // A rate that does not divide evenly, on purpose: 12.34 x 89,533 is 1,104,837.22, so
        // there is actually a fraction to round away. A round rate would make this pass whether
        // the rounding existed or not.
        var price = OrderPricing.Calculate(Inputs(
            [new PricedLine(12.34m, 1)], rate: 89_533m));

        const decimal exact = 12.34m * 89_533m;
        exact.ShouldNotBe(decimal.Truncate(exact), "otherwise this test proves nothing");

        // There is no smaller unit in circulation; fractions of a lira on a receipt are nonsense.
        price.TotalLbp.ShouldBe(1_104_837m);
        price.TotalLbp!.Value.ShouldBe(decimal.Truncate(price.TotalLbp.Value));
    }

    [Fact]
    public void No_configured_rate_means_no_pound_figure_rather_than_a_wrong_one()
    {
        var price = OrderPricing.Calculate(Inputs([new PricedLine(10m, 1)], rate: null));

        price.TotalLbp.ShouldBeNull();
    }

    // ------------------------------------------------------------------ rounding

    [Theory]
    [InlineData(1.005, 1.01)]
    [InlineData(1.015, 1.02)]
    [InlineData(2.675, 2.68)]
    [InlineData(-1.005, -1.01)]
    public void Halves_round_away_from_zero(decimal input, decimal expected) =>
        OrderPricing.Round(input).ShouldBe(expected);

    [Fact]
    public void Every_money_figure_fits_the_column_it_is_stored_in()
    {
        // decimal(10,2) everywhere. A third decimal place would be rounded on write, so a total
        // that survived this method but not the INSERT would be the worst kind of discrepancy.
        var price = OrderPricing.Calculate(Inputs(
            [new PricedLine(3.33m, 7)], deliveryFee: 2.75m, discount: 1.11m,
            commissionPercent: 17.5m, minOrder: 9.99m, rate: 89_500m));

        foreach (var amount in new[]
        {
            price.SubtotalUsd, price.DeliveryFeeUsd, price.TaxUsd, price.DiscountUsd,
            price.TotalUsd, price.CommissionUsd, price.ShortfallUsd,
        })
        {
            decimal.Round(amount, 2).ShouldBe(amount, $"{amount} does not fit decimal(10,2)");
        }
    }
}
