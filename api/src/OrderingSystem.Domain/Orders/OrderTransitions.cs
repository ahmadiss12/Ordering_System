using System.Collections.Frozen;
using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Domain.Orders;

/// <summary>One legal move, and the circumstances it is legal in.</summary>
/// <param name="From">The status the order is in now.</param>
/// <param name="To">The status it may move to.</param>
/// <param name="By">Who may make this move.</param>
/// <param name="OnlyFor">
/// The fulfillment type this move belongs to, or null when it applies to both. A pickup order
/// never goes out for delivery, and a delivery order is never ready for pickup.
/// </param>
public readonly record struct OrderTransition(
    OrderStatus From,
    OrderStatus To,
    OrderActor By,
    FulfillmentType? OnlyFor);

/// <summary>Why a move was refused. <see cref="Refusal.None"/> means it was not.</summary>
public enum Refusal
{
    None = 0,

    /// <summary>The order is already in that status. Usually a double-press, not a mistake.</summary>
    AlreadyThere,

    /// <summary>The order is finished. Nothing moves out of delivered, rejected or cancelled.</summary>
    Terminal,

    /// <summary>No such move exists for anybody, in any circumstance.</summary>
    NotPossible,

    /// <summary>The move exists, but for the other party.</summary>
    NotYours,

    /// <summary>The move exists, but belongs to the other fulfillment type.</summary>
    WrongFulfillment,

    /// <summary>Rejecting an order requires a reason that can be reported on.</summary>
    ReasonRequired,
}

/// <summary>
/// Every legal move an order can make, declared as data.
///
/// <para>
/// ADR-11 chose a table over a switch for one reason: a table can be enumerated. The test walks
/// the whole cross-product of status × status × actor × fulfillment and asserts that exactly
/// these rows are permitted and every one of the remaining two hundred-odd combinations is
/// refused. A switch can only be tested for the cases somebody remembered to write.
/// </para>
/// <para>
/// Nothing here touches the database. Applying a transition — writing the status, appending the
/// <c>OrderEvent</c>, honouring the rowversion — belongs to the application layer; this decides
/// only whether it may happen at all.
/// </para>
/// </summary>
public static class OrderTransitions
{
    /// <summary>An order in one of these is finished, and no move leaves it.</summary>
    public static readonly FrozenSet<OrderStatus> Terminal = new[]
    {
        OrderStatus.Delivered,
        OrderStatus.Rejected,
        OrderStatus.Cancelled,
    }.ToFrozenSet();

    private static readonly FrozenSet<OrderTransition> Allowed = new OrderTransition[]
    {
        // --- the restaurant answers a new order -------------------------------------------
        new(OrderStatus.Placed, OrderStatus.Accepted, OrderActor.Restaurant, null),

        // Refusing is only possible before accepting. Backing out afterwards is a cancellation,
        // not a rejection: the two mean different things in the rejection-rate report, and an
        // order the kitchen already started costs somebody something.
        new(OrderStatus.Placed, OrderStatus.Rejected, OrderActor.Restaurant, null),

        // --- the customer changes their mind ----------------------------------------------
        // Open until cooking starts, not until acceptance. Accepted means somebody saw the
        // order; Preparing means food is being made, and that is the point of no return.
        new(OrderStatus.Placed, OrderStatus.Cancelled, OrderActor.Customer, null),
        new(OrderStatus.Accepted, OrderStatus.Cancelled, OrderActor.Customer, null),

        // A restaurant that accepts and then cannot deliver — a power cut, an ingredient gone.
        // It happens, and without these two rows the order sits in Preparing forever, which is
        // worse for everyone than a cancellation somebody can see and report on. It carries a
        // reason for exactly that purpose.
        new(OrderStatus.Accepted, OrderStatus.Cancelled, OrderActor.Restaurant, null),
        new(OrderStatus.Preparing, OrderStatus.Cancelled, OrderActor.Restaurant, null),

        // --- the kitchen works ------------------------------------------------------------
        new(OrderStatus.Accepted, OrderStatus.Preparing, OrderActor.Restaurant, null),

        // The fork. One branch per fulfillment type, so the status always says something true
        // about where the food physically is.
        new(OrderStatus.Preparing, OrderStatus.ReadyForPickup, OrderActor.Restaurant, FulfillmentType.Pickup),
        new(OrderStatus.Preparing, OrderStatus.OutForDelivery, OrderActor.Restaurant, FulfillmentType.Delivery),

        // --- handed over ------------------------------------------------------------------
        new(OrderStatus.ReadyForPickup, OrderStatus.Delivered, OrderActor.Restaurant, FulfillmentType.Pickup),
        new(OrderStatus.OutForDelivery, OrderStatus.Delivered, OrderActor.Restaurant, FulfillmentType.Delivery),
    }.ToFrozenSet();

    /// <summary>Every declared move. Exposed so tests and documentation can enumerate them.</summary>
    public static IReadOnlyCollection<OrderTransition> All => Allowed;

    public static bool IsTerminal(OrderStatus status) => Terminal.Contains(status);

    /// <summary>
    /// Whether this move must carry a reason from the fixed list.
    ///
    /// <para>
    /// It depends on who is moving, not only on where to. A restaurant refusing an order or
    /// backing out of one it accepted is what the rejection-rate report counts, and a report
    /// cannot group by a sentence somebody typed. A customer changing their mind is nobody's
    /// business but theirs, and a form standing between them and the button would be rude.
    /// </para>
    /// </summary>
    public static bool RequiresReason(OrderStatus to, OrderActor by) =>
        to == OrderStatus.Rejected
        || (to == OrderStatus.Cancelled && by == OrderActor.Restaurant);

    /// <summary>
    /// What this party can do with this order right now. The dashboard draws its buttons from
    /// this, so a button that would be refused is never rendered in the first place.
    /// </summary>
    public static IReadOnlyList<OrderStatus> NextFor(
        OrderStatus from, FulfillmentType fulfillment, OrderActor by) =>
        [.. Allowed
            .Where(t => t.From == from && t.By == by && Applies(t, fulfillment))
            .Select(t => t.To)
            .Order()];

    /// <summary>
    /// Whether this move may happen, and if not, precisely why.
    ///
    /// <para>
    /// The reasons are separated rather than collapsed into one "no" because they are different
    /// answers: the wrong party is a permissions problem, the wrong fulfillment type is a bug in
    /// the caller, and a missing reason is a form the person has not finished filling in.
    /// </para>
    /// </summary>
    public static Refusal Check(
        OrderStatus from,
        OrderStatus to,
        FulfillmentType fulfillment,
        OrderActor by,
        bool hasReason = false)
    {
        if (from == to)
        {
            return Refusal.AlreadyThere;
        }

        if (IsTerminal(from))
        {
            return Refusal.Terminal;
        }

        var matching = Allowed.Where(t => t.From == from && t.To == to).ToArray();
        if (matching.Length == 0)
        {
            return Refusal.NotPossible;
        }

        // Fulfillment is checked before the actor: "a pickup order cannot go out for delivery"
        // describes the order, while "that is not yours to do" describes the caller, and the
        // first is the more useful thing to be told.
        if (!matching.Any(t => Applies(t, fulfillment)))
        {
            return Refusal.WrongFulfillment;
        }

        if (!matching.Any(t => t.By == by && Applies(t, fulfillment)))
        {
            return Refusal.NotYours;
        }

        return RequiresReason(to, by) && !hasReason ? Refusal.ReasonRequired : Refusal.None;
    }

    private static bool Applies(OrderTransition transition, FulfillmentType fulfillment) =>
        transition.OnlyFor is null || transition.OnlyFor == fulfillment;
}
