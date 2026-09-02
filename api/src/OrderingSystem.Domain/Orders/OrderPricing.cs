namespace OrderingSystem.Domain.Orders;

/// <summary>One line as it is being priced: what one costs, and how many.</summary>
public readonly record struct PricedLine(decimal UnitPriceUsd, int Quantity)
{
    /// <summary>
    /// Exact: a unit price has two decimal places and a quantity is a whole number, so this
    /// multiplication cannot introduce a third.
    /// </summary>
    public decimal TotalUsd => UnitPriceUsd * Quantity;
}

/// <summary>Everything the calculation needs, gathered by the caller so the maths stays pure.</summary>
public readonly record struct PricingInputs(
    IReadOnlyCollection<PricedLine> Lines,
    decimal DeliveryFeeUsd,
    decimal DiscountUsd,
    decimal CommissionPercent,
    int PrepMinutes,
    int TravelMinutes,
    decimal MinOrderUsd,
    decimal? ExchangeRateLbpPerUsd);

/// <summary>What the customer is told, and what the platform books.</summary>
public sealed record OrderPrice(
    decimal SubtotalUsd,
    decimal DeliveryFeeUsd,
    decimal TaxUsd,
    decimal DiscountUsd,
    decimal TotalUsd,
    decimal CommissionPercent,
    decimal CommissionUsd,
    int PromisedMinutesMin,
    int PromisedMinutesMax,
    decimal? TotalLbp,
    decimal MinOrderUsd,
    bool MeetsMinimum,
    decimal ShortfallUsd);

/// <summary>
/// The one place a total is worked out.
///
/// <para>
/// Pure and in the domain, like the state machine, because money is the part of this system that
/// must be inspectable without a database in the way. Every figure an order stores is produced
/// here, so a receipt and a settlement report cannot drift apart by each doing their own sums.
/// </para>
/// <para>
/// Decimal throughout, never double. A price of 0.10 has no exact binary representation, and a
/// menu adds prices together dozens of times per order.
/// </para>
/// </summary>
public static class OrderPricing
{
    /// <summary>Every money column is decimal(10,2); anything longer would be silently rounded on write.</summary>
    public const int MoneyDecimals = 2;

    /// <summary>
    /// How much wider than the earliest estimate the promise runs.
    ///
    /// A single number would be a promise to the minute, which no kitchen can keep and every
    /// customer would judge it by. Ten minutes is the window; it is one constant so that
    /// changing it is a decision rather than an archaeology exercise.
    /// </summary>
    public const int PromiseWindowMinutes = 10;

    public static OrderPrice Calculate(PricingInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs.Lines);

        var subtotal = Round(inputs.Lines.Sum(line => line.TotalUsd));
        var deliveryFee = Round(inputs.DeliveryFeeUsd);
        var discount = Round(inputs.DiscountUsd);

        // Zero, always, and named rather than dropped. The column exists so that charging
        // Lebanese VAT one day is a configuration change instead of a migration and a rewrite of
        // every historical total.
        const decimal tax = 0m;

        var total = subtotal + deliveryFee + tax - discount;

        // The platform's cut is taken on food, not on the delivery fee: the fee is what it costs
        // to get the order there, and charging commission on it would bill the restaurant for
        // the courier. This is a business rule, not arithmetic — see the plan.
        var commission = Round(subtotal * inputs.CommissionPercent / 100m);

        var earliest = inputs.PrepMinutes + inputs.TravelMinutes;

        return new OrderPrice(
            SubtotalUsd: subtotal,
            DeliveryFeeUsd: deliveryFee,
            TaxUsd: tax,
            DiscountUsd: discount,
            TotalUsd: total,
            CommissionPercent: inputs.CommissionPercent,
            CommissionUsd: commission,
            PromisedMinutesMin: earliest,
            PromisedMinutesMax: earliest + PromiseWindowMinutes,
            // Whole pounds. There is no smaller unit in circulation, and a receipt showing
            // fractions of a lira would be nonsense.
            TotalLbp: inputs.ExchangeRateLbpPerUsd is { } rate
                ? decimal.Round(total * rate, 0, MidpointRounding.AwayFromZero)
                : null,
            MinOrderUsd: inputs.MinOrderUsd,
            // Against the subtotal, not the total. A delivery fee is not food, and letting it
            // carry someone over the minimum would make the minimum meaningless.
            MeetsMinimum: subtotal >= inputs.MinOrderUsd,
            ShortfallUsd: Round(Math.Max(0m, inputs.MinOrderUsd - subtotal)));
    }

    /// <summary>
    /// Two places, rounding halves up.
    ///
    /// Away from zero rather than to-even: banker's rounding is right for repeated statistical
    /// sums and wrong on a receipt, where a customer who adds up the lines themselves must get
    /// the number they were charged.
    /// </summary>
    public static decimal Round(decimal value) =>
        decimal.Round(value, MoneyDecimals, MidpointRounding.AwayFromZero);
}
