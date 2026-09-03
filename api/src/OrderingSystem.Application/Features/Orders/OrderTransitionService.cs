using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Application.Features.Orders;

/// <summary>
/// Moving an order: accepting it, refusing it, cooking it, handing it over, and backing out.
///
/// <para>
/// The only write path an order has after checkout, and it decides nothing on its own. Whether a
/// move is legal belongs to <see cref="OrderStateMachine"/>; this loads the order, works out which
/// party is asking, and — if the state machine allows it — writes the new status and the trail
/// entry that records who did it, in one transaction.
/// </para>
/// <para>
/// Nothing here filters by tenant. The query filter on <c>Order</c> has already decided whether
/// this caller can see the order at all, and an order they cannot see is a 404 before any of this
/// runs.
/// </para>
/// </summary>
public sealed class OrderTransitionService(
    IAppDbContext db,
    ITenantContext tenant,
    ITenantGuard guard,
    IValidationService validation,
    IClock clock,
    OrderQueryService orders,
    IOrderNotifier notifier)
{
    public async Task<OrderDetailResponse> ChangeStatusAsync(
        Guid orderId, ChangeOrderStatusRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await validation.ValidateAsync(request, ct);
        var userId = guard.RequireUserId();

        // Tracked, unlike every read in OrderQueryService: the rowversion has to travel with the
        // entity for the concurrency check below to have anything to compare.
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException("No such order.");

        var actor = ResolveActor(order, request.To, userId);

        OrderStateMachine.EnsureAllowed(
            order.Status, request.To, order.FulfillmentType, actor, request.Reason);

        // A reason on a move that does not take one would land in the column the rejection-rate
        // report reads and quietly make that report wrong. Refused rather than dropped: silently
        // discarding what a caller sent is how a client ends up believing it was recorded.
        if (request.Reason is not null && !OrderTransitions.RequiresReason(request.To, actor))
        {
            throw new ValidationFailedException(
                $"An order being marked {OrderStateMachine.Describe(request.To)} does not take a reason.",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["reason"] = ["Leave this empty."],
                });
        }

        var from = order.Status;
        order.Status = request.To;

        if (request.Reason is not null)
        {
            // A rejection and a restaurant's cancellation both land here, so the report can ask
            // one question — which orders carry a reason — and get every order the restaurant
            // dropped, whichever way it dropped it. A customer changing their mind sets nothing,
            // which is what keeps them out of that report.
            order.RejectionReason = request.Reason;
            order.RejectionNote = request.Note;
        }

        db.OrderEvents.Add(new OrderEvent
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FromStatus = from,
            ToStatus = request.To,
            ChangedByUserId = userId,
            Note = request.Note,
            CreatedAt = clock.UtcNow,
        });

        try
        {
            // One SaveChanges, so the status and the trail entry are one atomic write. An order
            // whose status moved without a matching event would be a gap nobody could explain.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(await RaceMessageAsync(orderId, ct));
        }

        // After the commit, never inside it, and after the concurrency check in particular: the
        // tablet that lost the race must not announce a move it did not make.
        await notifier.OrderChangedAsync(order.RestaurantId, order.CustomerId,
            new OrderChanged(order.Id, order.OrderNumber, request.To, from, clock.UtcNow), ct);

        // The whole order back, not just the new status: the screen that pressed the button wants
        // the refreshed trail and the next set of buttons, and asking for them separately would
        // leave a window where it is showing one and acting on the other.
        return await orders.ByIdAsync(orderId, ct);
    }

    /// <summary>
    /// Which party this caller is, for this particular move.
    ///
    /// <para>
    /// Nearly always obvious: staff at the restaurant cooking the order are the restaurant, and
    /// whoever placed it is the customer. The exception is somebody who is both — a cook ordering
    /// their own lunch — and there the move decides which hat they are wearing. Only the
    /// restaurant can accept an order and only the customer can cancel a placed one, and the
    /// transition table already knows which is which, so it is asked rather than guessed.
    /// </para>
    /// </summary>
    private OrderActor ResolveActor(Order order, OrderStatus to, Guid userId)
    {
        var forRestaurant = tenant.RestaurantId == order.RestaurantId;
        var forCustomer = order.CustomerId == userId;

        if (forRestaurant && (!forCustomer || RestaurantCanMakeThisMove(order, to)))
        {
            return OrderActor.Restaurant;
        }

        if (forCustomer)
        {
            return OrderActor.Customer;
        }

        // A platform admin, who can see every order and moves none of them. There is deliberately
        // no OrderActor for the platform: nothing in the product lets it accept or cancel on a
        // restaurant's behalf, and inventing an actor the table has no rows for would be a hole
        // in the one place this system cannot afford one.
        throw new ForbiddenException("Only the customer or the restaurant can move an order.");
    }

    private static bool RestaurantCanMakeThisMove(Order order, OrderStatus to) =>
        OrderTransitions
            .NextFor(order.Status, order.FulfillmentType, OrderActor.Restaurant)
            .Contains(to);

    /// <summary>
    /// Two tablets, one order. The rowversion means the second write fails rather than both
    /// appearing to succeed; this turns that failure into something a person can act on by saying
    /// where the order actually is now.
    /// </summary>
    private async Task<string> RaceMessageAsync(Guid orderId, CancellationToken ct)
    {
        // AsNoTracking on purpose. The tracked copy is the stale one whose write just failed, and
        // identity resolution would hand it straight back.
        var current = await db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => (OrderStatus?)o.Status)
            .FirstOrDefaultAsync(ct);

        return current is null
            ? "Somebody else changed this order while you were looking at it. Refresh and try again."
            : "Somebody else moved this order while you were looking at it — it is now "
              + $"{OrderStateMachine.Describe(current.Value)}. Refresh and try again.";
    }
}
