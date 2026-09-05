using System.Diagnostics.CodeAnalysis;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Geography;
using OrderingSystem.Domain.Identity;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Domain.Orders;

/// <summary>
/// A placed order. Immutable in substance once created: totals, commission, the exchange rate and
/// the delivery address are all frozen here rather than read back from the live records they came
/// from, so a price rise, a renamed dish or an edited address can never rewrite what was sold.
/// </summary>
public class Order
{
    public Guid Id { get; set; }

    /// <summary>Human-readable reference shown to customer and kitchen. Unique across the platform.</summary>
    public string OrderNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }
    public Guid RestaurantId { get; set; }

    /// <summary>
    /// Null for pickup. Kept for reporting only — everything needed to deliver the order is in
    /// the snapshot fields below, because the customer may edit or delete this address later.
    /// </summary>
    public Guid? AddressId { get; set; }

    public FulfillmentType FulfillmentType { get; set; }
    public OrderStatus Status { get; set; }

    // ---- delivery address as it was at the moment of ordering ----

    public string? DeliveryZoneName { get; set; }
    public string? DeliveryLine1 { get; set; }
    public string? DeliveryBuilding { get; set; }
    public string? DeliveryFloor { get; set; }
    public string? DeliveryLandmark { get; set; }
    public decimal? DeliveryLat { get; set; }
    public decimal? DeliveryLng { get; set; }

    // ---- payment, tracked independently of Status ----

    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; }

    // ---- money, all USD, all decimal(10,2) ----

    /// <summary>Sum of the order lines. Minimum-order checks run against this, not the total.</summary>
    public decimal SubtotalUsd { get; set; }

    public decimal DeliveryFeeUsd { get; set; }

    /// <summary>
    /// Always zero — no tax was in scope. The column stays so that charging VAT later is a
    /// configuration change, not a migration plus a rewrite of every historical total.
    /// </summary>
    public decimal TaxUsd { get; set; }

    public decimal DiscountUsd { get; set; }

    /// <summary>Subtotal + delivery fee + tax − discount. Stored, not computed on read.</summary>
    public decimal TotalUsd { get; set; }

    /// <summary>
    /// Rate in force when the order was placed. The customer's receipt shows the same LBP figure
    /// forever, however far the rate moves afterwards.
    /// </summary>
    public decimal ExchangeRateLbp { get; set; }

    /// <summary>
    /// The restaurant's rate at the time, copied here. Changing a restaurant's commission must
    /// never silently restate past settlement.
    /// </summary>
    public decimal CommissionPercent { get; set; }

    /// <summary>Money value of the commission, so settlement never re-derives it from a percentage.</summary>
    public decimal CommissionUsd { get; set; }

    // ---- promise made to the customer ----

    /// <summary>Prep time plus the zone's travel estimate, frozen so the promise can be judged after the fact.</summary>
    public int PromisedMinutesMin { get; set; }
    public int PromisedMinutesMax { get; set; }

    // ---- notes and refusal ----

    public string? CustomerNote { get; set; }

    /// <summary>
    /// Why the restaurant dropped the order. Required when <see cref="Status"/> is Rejected, and
    /// also set when a restaurant cancels one it had already accepted — the same fixed list, so the
    /// rejection-rate report can ask one question and get every order the restaurant dropped,
    /// whichever way it dropped it. Null when the customer changed their mind, which keeps them
    /// out of that report.
    /// </summary>
    public RejectionReason? RejectionReason { get; set; }

    /// <summary>Optional detail alongside the reason. Never a substitute for it.</summary>
    public string? RejectionNote { get; set; }

    // ---- concurrency and de-duplication ----

    /// <summary>
    /// Supplied by the client, one per checkout attempt. A unique index on it means a double-tap
    /// on a poor connection returns the original order rather than creating a second one.
    /// </summary>
    public Guid IdempotencyKey { get; set; }

    /// <summary>
    /// SQL Server rowversion. Two staff pressing Accept on two tablets: the second write fails
    /// and the screen refreshes, instead of both appearing to succeed.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "SQL Server rowversion is an 8-byte value; byte[] is the shape EF Core maps it to.")]
    public byte[] RowVersion { get; set; } = [];

    public DateTimeOffset PlacedAt { get; set; }

    /// <summary>
    /// The restaurant's own calendar day, as printed in <see cref="OrderNumber"/>.
    ///
    /// <para>
    /// Stored rather than derived from <see cref="PlacedAt"/>, which is UTC. Beirut runs two or
    /// three hours ahead depending on the season, so an order taken at half past midnight is on
    /// the previous UTC date — and a report grouped by that would put a Saturday evening's
    /// takings partly under Friday, differently in winter and summer.
    /// </para>
    /// <para>
    /// It is not new information either: checkout already computes this day to build the order
    /// number, and this only stops it being thrown away. Keeping it means the day a report puts
    /// an order under is the same day the customer can read off their own order number.
    /// </para>
    /// </summary>
    public DateOnly BusinessDate { get; set; }

    public User Customer { get; set; } = null!;
    public Restaurant Restaurant { get; set; } = null!;
    public Address? Address { get; set; }
    public ICollection<OrderLine> Lines { get; } = [];
    public ICollection<OrderEvent> Events { get; } = [];
    public ICollection<Payment> Payments { get; } = [];
}
