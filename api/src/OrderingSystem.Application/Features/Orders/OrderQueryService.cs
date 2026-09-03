using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Application.Common;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Application.Features.Orders;

/// <summary>
/// Reading orders — a customer's history, a kitchen's queue, and one order in full.
///
/// <para>
/// Nothing here writes, and nothing here filters by tenant. The query filter on <c>Order</c> has
/// already decided what this caller may see: their own orders as a customer, their restaurant's
/// as staff, everything as a platform admin. Restating it in every method would be a second
/// place for the rule to be wrong.
/// </para>
/// </summary>
public sealed class OrderQueryService(IAppDbContext db, ITenantContext tenant)
{
    /// <summary>
    /// The caller's own order history, newest first.
    ///
    /// Explicitly by customer id as well as by the filter: a restaurant owner is staff and a
    /// customer at once, and this list is the customer half of them.
    /// </summary>
    public async Task<PagedResult<OrderSummaryResponse>> MineAsync(
        int? page, int? pageSize, CancellationToken ct = default)
    {
        var userId = tenant.UserId ?? throw new AuthenticationFailedException("Sign in to see your orders.");

        return await PageAsync(
            db.Orders.AsNoTracking().Where(o => o.CustomerId == userId),
            OrderActor.Customer, oldestFirst: false, page, pageSize, ct);
    }

    /// <summary>
    /// The restaurant's queue. Statuses are a filter rather than a fixed set, because a kitchen
    /// screen wants the live ones and a history screen wants the finished ones.
    /// </summary>
    /// <param name="newestFirst">
    /// True for the history screen, where somebody is looking for what happened yesterday. False —
    /// the default, and what the kitchen queue gets — for a list that is worked from the top.
    /// </param>
    public async Task<PagedResult<OrderSummaryResponse>> ForRestaurantAsync(
        IReadOnlyCollection<OrderStatus>? statuses, bool newestFirst,
        int? page, int? pageSize, CancellationToken ct = default)
    {
        var restaurantId = tenant.RestaurantId
            ?? throw new ForbiddenException("Only restaurant staff can see a restaurant's orders.");

        var query = db.Orders.AsNoTracking().Where(o => o.RestaurantId == restaurantId);

        if (statuses is { Count: > 0 })
        {
            query = query.Where(o => statuses.Contains(o.Status));
        }

        // Which end the list is read from is the caller's to say, and it has to be the server that
        // applies it: a queue sorted the wrong way would put the orders most in need of attention
        // on the last page, and a history sorted the wrong way would open on the restaurant's
        // first ever order.
        return await PageAsync(
            query, OrderActor.Restaurant, oldestFirst: !newestFirst, page, pageSize, ct);
    }

    /// <summary>
    /// One order, in full.
    ///
    /// Not found rather than forbidden when it is somebody else's: the query filter has already
    /// hidden it, and distinguishing "not yours" from "does not exist" would confirm to a
    /// stranger that an order number is real.
    /// </summary>
    public async Task<OrderDetailResponse> ByIdAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.Status,
                o.FulfillmentType,
                o.PlacedAt,
                o.CustomerId,
                o.RestaurantId,
                RestaurantName = o.Restaurant.Name,
                RestaurantSlug = o.Restaurant.Slug,
                RestaurantPhone = o.Restaurant.Phone,
                CustomerName = o.Customer.FullName,
                o.SubtotalUsd,
                o.DeliveryFeeUsd,
                o.TaxUsd,
                o.DiscountUsd,
                o.TotalUsd,
                o.ExchangeRateLbp,
                o.PaymentMethod,
                o.PaymentStatus,
                o.PromisedMinutesMin,
                o.PromisedMinutesMax,
                o.CustomerNote,
                o.RejectionReason,
                o.RejectionNote,
                o.DeliveryZoneName,
                o.DeliveryLine1,
                o.DeliveryBuilding,
                o.DeliveryFloor,
                o.DeliveryLandmark,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("No such order.");

        var lines = await db.OrderLines.AsNoTracking()
            .Where(l => l.OrderId == orderId)
            .Select(l => new OrderLineResponse(
                l.Id,
                l.ItemNameSnapshot,
                l.Quantity,
                l.UnitPriceUsd,
                l.LineTotalUsd,
                l.Note,
                l.SelectedOptions
                    .OrderBy(o => o.GroupNameSnapshot)
                    .Select(o => new OrderLineOptionResponse(
                        o.GroupNameSnapshot, o.OptionNameSnapshot, o.PriceDeltaUsd, o.Quantity))
                    .ToList()))
            .ToListAsync(ct);

        var events = await db.OrderEvents.AsNoTracking()
            .Where(e => e.OrderId == orderId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new OrderEventResponse(
                e.FromStatus, e.ToStatus, e.ChangedByUser!.FullName, e.Note, e.CreatedAt))
            .ToListAsync(ct);

        // Which party is asking decides what they may do next, and one person can be both — a
        // cook ordering their own lunch is staff and customer at once. Both sets are offered in
        // that case, because OrderTransitionService accepts either from them, and a screen that
        // drew fewer buttons than the API would honour would be quietly wrong.
        var moves = new List<OrderStatus>();

        if (tenant.RestaurantId == order.RestaurantId)
        {
            moves.AddRange(OrderTransitions.NextFor(
                order.Status, order.FulfillmentType, OrderActor.Restaurant));
        }

        if (order.CustomerId == tenant.UserId)
        {
            moves.AddRange(OrderTransitions.NextFor(
                order.Status, order.FulfillmentType, OrderActor.Customer));
        }

        return new OrderDetailResponse(
            order.Id,
            order.OrderNumber,
            order.Status,
            order.FulfillmentType,
            order.PlacedAt,
            order.RestaurantName,
            order.RestaurantSlug,
            order.RestaurantPhone,
            order.CustomerName,
            order.SubtotalUsd,
            order.DeliveryFeeUsd,
            order.TaxUsd,
            order.DiscountUsd,
            order.TotalUsd,
            // The rate frozen at checkout, so a receipt shows the same figure forever however far
            // the rate moves afterwards.
            order.ExchangeRateLbp == 0m
                ? null
                : decimal.Round(order.TotalUsd * order.ExchangeRateLbp, 0, MidpointRounding.AwayFromZero),
            order.PaymentMethod,
            order.PaymentStatus,
            order.PromisedMinutesMin,
            order.PromisedMinutesMax,
            order.CustomerNote,
            order.RejectionReason,
            order.RejectionNote,
            order.FulfillmentType == FulfillmentType.Delivery
                ? new DeliveryAddressResponse(
                    order.DeliveryZoneName, order.DeliveryLine1, order.DeliveryBuilding,
                    order.DeliveryFloor, order.DeliveryLandmark)
                : null,
            lines,
            events,
            [.. moves.Distinct().Order()]);
    }

    /// <param name="actor">
    /// Which party this list is for, so each row can carry the moves that party may make. A
    /// customer's history offers cancelling; a kitchen's queue offers the rest.
    /// </param>
    /// <param name="oldestFirst">
    /// True for a kitchen's queue, where the order that has waited longest is the one that needs
    /// attention. False for a customer's history, where they are looking for last night's order.
    /// </param>
    private static async Task<PagedResult<OrderSummaryResponse>> PageAsync(
        IQueryable<Domain.Orders.Order> query, OrderActor actor, bool oldestFirst,
        int? page, int? pageSize, CancellationToken ct)
    {
        var (currentPage, size) = Paging.Normalise(page, pageSize);

        var total = await query.CountAsync(ct);

        var ordered = oldestFirst
            ? query.OrderBy(o => o.PlacedAt)
            : query.OrderByDescending(o => o.PlacedAt);

        // Projected to an anonymous type first, then finished in memory. The transitions come from
        // a frozen table rather than from the database, and no expression tree can call into it.
        var rows = await ordered
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.Status,
                o.FulfillmentType,
                o.PlacedAt,
                o.TotalUsd,
                ItemCount = o.Lines.Sum(l => l.Quantity),
                o.PromisedMinutesMin,
                o.PromisedMinutesMax,
                RestaurantName = o.Restaurant.Name,
                RestaurantSlug = o.Restaurant.Slug,
                CustomerName = o.Customer.FullName,
                o.RejectionReason,
            })
            .ToListAsync(ct);

        var items = rows
            .Select(o => new OrderSummaryResponse(
                o.Id,
                o.OrderNumber,
                o.Status,
                o.FulfillmentType,
                o.PlacedAt,
                o.TotalUsd,
                o.ItemCount,
                o.PromisedMinutesMin,
                o.PromisedMinutesMax,
                o.RestaurantName,
                o.RestaurantSlug,
                o.CustomerName,
                o.RejectionReason,
                OrderTransitions.NextFor(o.Status, o.FulfillmentType, actor)))
            .ToList();

        return new PagedResult<OrderSummaryResponse>(items, currentPage, size, total);
    }
}
