using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Application.Features.Orders;

/// <summary>
/// Drives an order through its lifecycle: authorises the caller, hands the transition to the
/// state machine, and persists the order and its new event together.
/// <para>
/// The division of labour is deliberate. <see cref="OrderStateMachine"/> knows what is legal and
/// nothing else; this service knows who is asking and where the rows live. Neither knows an HTTP
/// status code.
/// </para>
/// </summary>
public sealed class OrderStatusService(
    IAppDbContext db,
    IValidationService validation,
    ITenantContext tenant,
    IClock clock)
{
    public async Task<OrderStatusResponse> AdvanceAsync(
        Guid orderId, AdvanceOrderStatusRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        // IgnoreQueryFilters, deliberately. Read through the filter, another restaurant's order
        // is simply invisible and this method answers 404 — which tells a prober that any id they
        // cannot see does not exist. ADR-07 requires 403 for someone else's resource, so the row
        // has to be fetched before the ownership question can be answered. This is exactly the
        // "explicit ownership check" the ADR names as the second layer, and it is why the filter
        // alone was never considered sufficient.
        var order = await db.Orders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException("No order with that id.");

        EnsureCallerMay(order, request.To);

        var now = clock.UtcNow;
        OrderStateMachine.Transition(
            order, request.To, now, tenant.UserId, request.RejectionReason, request.Note);

        try
        {
            // One call, one transaction: the order's new status and its OrderEvent are written
            // together or not at all.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Order.RowVersion did its job. Two staff on two tablets both pressed Accept; the
            // second write lost and must not be retried silently, because the order is no longer
            // in the state this caller was looking at.
            throw new ConflictException(
                "This order was changed by someone else while you were working on it. "
                + "Refresh and try again.");
        }

        return new OrderStatusResponse(
            order.Id,
            order.OrderNumber,
            order.Status,
            OrderTransitions.NextFrom(order.Status, order.FulfillmentType),
            now);
    }

    /// <summary>
    /// Who may drive which part of the lifecycle. Cancelling belongs to the customer and
    /// rejecting belongs to the restaurant, and keeping them apart is what makes the
    /// rejection-rate report mean anything — a restaurant able to "cancel" would never appear in
    /// it, and a struggling kitchen would stay invisible.
    /// </summary>
    private void EnsureCallerMay(Order order, OrderStatus to)
    {
        if (tenant.IsPlatformAdmin)
        {
            return;
        }

        // Both are asked independently rather than as an either/or, because they can both be
        // true: a restaurant owner ordering from their own restaurant is staff here AND the
        // customer. Treating a non-null restaurant_id claim as "therefore not a customer" is the
        // bug that would lock an owner out of cancelling their own lunch.
        var isOwnOrder = tenant.UserId is { } userId && userId == order.CustomerId;
        var isStaffHere = tenant.RestaurantId is { } restaurantId && restaurantId == order.RestaurantId;

        if (!isOwnOrder && !isStaffHere)
        {
            throw new ForbiddenException("This order belongs to another restaurant or customer.");
        }

        if (to == OrderStatus.Cancelled && !isOwnOrder)
        {
            throw new ForbiddenException(
                "A restaurant rejects an order rather than cancelling it. Use Rejected with a reason.");
        }

        if (to != OrderStatus.Cancelled && !isStaffHere)
        {
            throw new ForbiddenException("Only the restaurant may advance an order beyond cancelling it.");
        }
    }
}
