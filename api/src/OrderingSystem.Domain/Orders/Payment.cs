using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Domain.Orders;

/// <summary>
/// One payment attempt against an order. An order may have several — a failed card attempt
/// followed by a successful one, or a payment later followed by a refund.
/// <para>
/// <see cref="Order.PaymentStatus"/> is the current summary and the field queries filter on;
/// these rows are the history behind it. The summary is derived from them, never the reverse.
/// </para>
/// </summary>
public class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }

    public PaymentMethod Method { get; set; }

    public decimal AmountUsd { get; set; }

    public PaymentStatus Status { get; set; }

    /// <summary>The gateway's own reference. Null for cash, which has no provider.</summary>
    public string? ProviderRef { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Order Order { get; set; } = null!;
}
