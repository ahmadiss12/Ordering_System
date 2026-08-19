namespace OrderingSystem.Domain.Orders;

/// <summary>
/// The counter behind human-readable order numbers, keyed by (restaurant, business date).
/// <para>
/// Numbers reset daily so they stay short enough to say out loud — a kitchen calls "order
/// forty-two", not "order forty-eight thousand two hundred and seventeen", which is where a
/// per-restaurant running total ends up after a couple of years.
/// </para>
/// <para>
/// Allocation is a single atomic UPDATE ... OUTPUT against this row, so two simultaneous
/// checkouts cannot receive the same number. Gaps are expected and harmless: a rolled-back
/// checkout consumes a number that no order ever uses. That would matter for invoice numbering,
/// which some jurisdictions require to be gapless, and does not matter here.
/// </para>
/// </summary>
public class OrderNumberSequence
{
    public Guid RestaurantId { get; set; }

    /// <summary>
    /// The restaurant's local calendar date, not UTC. An order taken at 00:30 belongs to that
    /// calendar day; a kitchen open past midnight will see its numbering reset mid-shift, which
    /// is the accepted trade for keeping the date in the number honest.
    /// </summary>
    public DateOnly BusinessDate { get; set; }

    /// <summary>Next number to hand out. Starts at 1 for each new (restaurant, date) pair.</summary>
    public int NextValue { get; set; } = 1;
}
