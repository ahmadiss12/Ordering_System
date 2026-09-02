using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;

namespace OrderingSystem.Domain.Orders;

/// <summary>
/// The gate every status change goes through.
///
/// <para>
/// Separate from <see cref="OrderTransitions"/> on purpose: that answers "is this legal", which
/// a screen wants in order to decide what to draw, and this answers "let it happen or stop it",
/// which a use case wants. A screen asking the throwing version would have to catch exceptions
/// to render a button.
/// </para>
/// <para>
/// Each refusal throws a different exception because they are genuinely different failures, and
/// the API's one exception handler turns them into 403, 409 and 400 without any endpoint
/// choosing a status code.
/// </para>
/// </summary>
public static class OrderStateMachine
{
    /// <summary>
    /// Throws unless the move may happen. Returns nothing: there is no partial success here.
    /// </summary>
    /// <exception cref="ConflictException">The order is not in a state that allows this.</exception>
    /// <exception cref="ForbiddenException">The move belongs to the other party.</exception>
    /// <exception cref="ValidationFailedException">A rejection arrived without a reason.</exception>
    public static void EnsureAllowed(
        OrderStatus from,
        OrderStatus to,
        FulfillmentType fulfillment,
        OrderActor by,
        RejectionReason? reason = null)
    {
        var refusal = OrderTransitions.Check(from, to, fulfillment, by, reason is not null);

        switch (refusal)
        {
            case Refusal.None:
                return;

            // A double-press, most often. Said plainly so the interface can shrug rather than
            // showing somebody an error for pressing a button that had already worked.
            case Refusal.AlreadyThere:
                throw new ConflictException($"This order is already {Describe(to)}.");

            case Refusal.Terminal:
                throw new ConflictException(
                    $"This order is {Describe(from)} and cannot change any further.");

            case Refusal.NotPossible:
                throw new ConflictException(
                    $"An order cannot go from {Describe(from)} to {Describe(to)}.");

            case Refusal.WrongFulfillment:
                throw new ConflictException(
                    fulfillment == FulfillmentType.Pickup
                        ? "This is a pickup order, so it is collected rather than delivered."
                        : "This is a delivery order, so it is not collected from the counter.");

            case Refusal.NotYours:
                throw new ForbiddenException(
                    by == OrderActor.Customer
                        ? "Only the restaurant can make that change."
                        : "Only the customer can make that change.");

            case Refusal.ReasonRequired:
                throw new ValidationFailedException(
                    "A rejected order needs a reason.",
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["reason"] = ["Choose why the order is being refused."],
                    });

            default:
                // Unreachable while every Refusal member is handled above. Present so that
                // adding one and forgetting it here fails loudly instead of silently allowing
                // a transition nobody meant to permit.
                throw new ConflictException("This order cannot change in that way.");
        }
    }

    /// <summary>Status wording for a sentence a customer or a cook will read.</summary>
    public static string Describe(OrderStatus status) => status switch
    {
        OrderStatus.Placed => "placed",
        OrderStatus.Accepted => "accepted",
        OrderStatus.Preparing => "being prepared",
        OrderStatus.ReadyForPickup => "ready for pickup",
        OrderStatus.OutForDelivery => "out for delivery",
        OrderStatus.Delivered => "delivered",
        OrderStatus.Rejected => "rejected",
        OrderStatus.Cancelled => "cancelled",
        _ => status.ToString().ToLowerInvariant(),
    };
}
