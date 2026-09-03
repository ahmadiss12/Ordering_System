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
            page, pageSize, ct);
    }

    /// <summary>
    /// The restaurant's queue. Statuses are a filter rather than a fixed set, because a kitchen
    /// screen wants the live ones and a history screen wants the finished ones.
    /// </summary>
    public async Task<PagedResult<OrderSummaryResponse>> ForRestaurantAsync(
        IReadOnlyCollection<OrderStatus>? statuses, int? page, int? pageSize, CancellationToken ct = default)
    {
        var restaurantId = tenant.RestaurantId
            ?? throw new ForbiddenException("Only restaurant staff can see a restaurant's orders.");

        var query = db.Orders.AsNoTracking().Where(o => o.RestaurantId == restaurantId);

        if (statuses is { Count: > 0 })
        {
            query = query.Where(o => statuses.Contains(o.Status));
        }

        return await PageAsync(query, page, pageSize, ct);
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

        // Which party is asking decides what they may do next. Staff at this restaurant act as
        // the restaurant; anybody else looking at their own order acts as the customer.
        var actor = tenant.RestaurantId == order.RestaurantId
            ? OrderActor.Restaurant
            : OrderActor.Customer;

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
            OrderTransitions.NextFor(order.Status, order.FulfillmentType, actor));
    }

    private static async Task<PagedResult<OrderSummaryResponse>> PageAsync(
        IQueryable<Domain.Orders.Order> query, int? page, int? pageSize, CancellationToken ct)
    {
        var (currentPage, size) = Paging.Normalise(page, pageSize);

        var total = await query.CountAsync(ct);

        var items = await query
            // Newest first everywhere. A kitchen works the top of the list and a customer looks
            // for what they ordered last night.
            .OrderByDescending(o => o.PlacedAt)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(o => new OrderSummaryResponse(
                o.Id,
                o.OrderNumber,
                o.Status,
                o.FulfillmentType,
                o.PlacedAt,
                o.TotalUsd,
                o.Lines.Sum(l => l.Quantity),
                o.Restaurant.Name,
                o.Restaurant.Slug,
                o.Customer.FullName))
            .ToListAsync(ct);

        return new PagedResult<OrderSummaryResponse>(items, currentPage, size, total);
    }
}
