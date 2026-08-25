using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;

namespace OrderingSystem.Domain.Orders;

/// <summary>
/// Validates a status change, applies it, and produces the <see cref="OrderEvent"/> that records
/// it — see ADR-11. Pure: it takes the moment rather than reading a clock, and it saves nothing,
/// so the whole lifecycle is testable with no database, no HTTP and no mocking framework.
/// <para>
/// The caller is responsible for persisting. One <c>SaveChangesAsync</c> writes the order and its
/// new event together, which is the "in one transaction" half of the decision — an order whose
/// status moved without a matching event would make the admin timeline lie.
/// </para>
/// </summary>
public static class OrderStateMachine
{
    /// <summary>
    /// Moves <paramref name="order"/> to <paramref name="to"/> and returns the event describing
    /// the move. Throws rather than returning a result object, because every caller would
    /// immediately turn a false into the same exception.
    /// </summary>
    /// <exception cref="ConflictException">The transition is not legal from the current status.</exception>
    /// <exception cref="ValidationFailedException">A rejection reason is missing, or given where it does not belong.</exception>
    public static OrderEvent Transition(
        Order order,
        OrderStatus to,
        DateTimeOffset at,
        Guid? changedByUserId,
        RejectionReason? rejectionReason = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(order);

        var from = order.Status;

        // Answered before the table is consulted, so a repeated click gets "already Accepted"
        // rather than the misleading "cannot move from Accepted to Accepted".
        if (from == to)
        {
            throw new ConflictException($"This order is already {to}.");
        }

        if (OrderTransitions.IsTerminal(from))
        {
            throw new ConflictException($"{from} is a final status. This order cannot change again.");
        }

        if (!OrderTransitions.IsAllowed(from, to, order.FulfillmentType))
        {
            throw new ConflictException(DescribeRefusal(from, to, order.FulfillmentType));
        }

        // The database enforces this too (CK_Orders_RejectionReasonRequired). It is checked here
        // as well so the caller gets a named field back instead of a constraint-violation 500.
        if (to == OrderStatus.Rejected && rejectionReason is null)
        {
            throw Invalid(nameof(rejectionReason), "A reason must be given when rejecting an order.");
        }

        if (to != OrderStatus.Rejected && rejectionReason is not null)
        {
            throw Invalid(nameof(rejectionReason), "A rejection reason applies only when rejecting an order.");
        }

        order.Status = to;

        if (to == OrderStatus.Rejected)
        {
            order.RejectionReason = rejectionReason;
            order.RejectionNote = note;
        }

        // Cash is collected at the door, so delivery is the moment it is paid. Guarded on Pending
        // rather than applied blindly: an order already marked Failed or Refunded must not be
        // quietly restated as Paid by a status change.
        if (to == OrderStatus.Delivered
            && order.PaymentMethod == PaymentMethod.CashOnDelivery
            && order.PaymentStatus == PaymentStatus.Pending)
        {
            order.PaymentStatus = PaymentStatus.Paid;
        }

        var moment = new OrderEvent
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FromStatus = from,
            ToStatus = to,
            ChangedByUserId = changedByUserId,
            Note = note,
            CreatedAt = at,
        };

        order.Events.Add(moment);
        return moment;
    }

    /// <summary>
    /// Names the legal alternatives in the message. A refusal that only says "no" sends the
    /// reader to the source; one that lists what would have worked usually ends the question.
    /// </summary>
    private static string DescribeRefusal(OrderStatus from, OrderStatus to, FulfillmentType fulfillment)
    {
        var legal = OrderTransitions.NextFrom(from, fulfillment);

        return legal.Count == 0
            ? $"A {fulfillment} order cannot move from {from} to {to}."
            : $"A {fulfillment} order cannot move from {from} to {to}. "
              + $"Legal next: {string.Join(", ", legal.Select(status => status.ToString()))}.";
    }

    private static ValidationFailedException Invalid(string field, string message) =>
        new("One or more fields are invalid.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
