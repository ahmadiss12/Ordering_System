using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Domain.Orders;

/// <summary>
/// The order lifecycle, declared as data rather than as branching code — see ADR-11.
/// <para>
/// The point of a table over a <c>switch</c> is that it can be enumerated. A test can walk the
/// full cross-product of statuses and fulfilment types and assert that exactly the intended pairs
/// are permitted and every other pair is refused, which is what "every legal and illegal
/// transition" actually requires. A <c>switch</c> cannot be enumerated, so its tests can only
/// cover the cases somebody remembered to write down.
/// </para>
/// </summary>
public static class OrderTransitions
{
    /// <summary>
    /// Every edge in the lifecycle. Nine of them, and adding a tenth should mean adding one line
    /// here and one line to the expected set in <c>OrderTransitionTests</c> — nowhere else.
    /// </summary>
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> Allowed =
    [
        (OrderStatus.Placed, OrderStatus.Accepted),
        (OrderStatus.Placed, OrderStatus.Rejected),
        (OrderStatus.Placed, OrderStatus.Cancelled),

        (OrderStatus.Accepted, OrderStatus.Preparing),
        (OrderStatus.Accepted, OrderStatus.Cancelled),

        (OrderStatus.Preparing, OrderStatus.ReadyForPickup),
        (OrderStatus.Preparing, OrderStatus.OutForDelivery),

        (OrderStatus.ReadyForPickup, OrderStatus.Delivered),
        (OrderStatus.OutForDelivery, OrderStatus.Delivered),
    ];

    /// <summary>
    /// Statuses nothing leaves. Kept separate from <see cref="Allowed"/> rather than inferred from
    /// it: "has no outgoing edge" and "is final" happen to coincide today, and a status that is
    /// merely unreachable-onward by oversight would silently become terminal.
    /// </summary>
    private static readonly HashSet<OrderStatus> TerminalStatuses =
    [
        OrderStatus.Delivered,
        OrderStatus.Rejected,
        OrderStatus.Cancelled,
    ];

    /// <summary>
    /// Statuses that only make sense for one fulfilment type. A pickup order is never
    /// OutForDelivery and a delivery order is never ReadyForPickup, so the same edge list serves
    /// both without a second table.
    /// </summary>
    private static readonly Dictionary<OrderStatus, FulfillmentType> BoundToFulfillment = new()
    {
        [OrderStatus.ReadyForPickup] = FulfillmentType.Pickup,
        [OrderStatus.OutForDelivery] = FulfillmentType.Delivery,
    };

    /// <summary>Nothing leaves this status. Delivered, Rejected and Cancelled are all final.</summary>
    public static bool IsTerminal(OrderStatus status) => TerminalStatuses.Contains(status);

    /// <summary>
    /// Both ends are checked against the fulfilment type, not only the destination. Checking the
    /// destination alone would leave ReadyForPickup → Delivered legal for a delivery order —
    /// unreachable in practice, but it would make this table describe states that cannot exist.
    /// </summary>
    public static bool IsAllowed(OrderStatus from, OrderStatus to, FulfillmentType fulfillment) =>
        Allowed.Contains((from, to))
        && SuitsFulfillment(from, fulfillment)
        && SuitsFulfillment(to, fulfillment);

    /// <summary>
    /// What this order may become next. Returned to the client so a dashboard renders the buttons
    /// that exist rather than every button plus a disabled state guessed on the front end.
    /// </summary>
    public static IReadOnlyList<OrderStatus> NextFrom(OrderStatus from, FulfillmentType fulfillment) =>
        Allowed
            .Where(edge => edge.From == from && IsAllowed(from, edge.To, fulfillment))
            .Select(edge => edge.To)
            .OrderBy(status => status)
            .ToArray();

    private static bool SuitsFulfillment(OrderStatus status, FulfillmentType fulfillment) =>
        !BoundToFulfillment.TryGetValue(status, out var required) || required == fulfillment;
}
