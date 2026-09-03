using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Application.Features.Cart;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Orders;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Application.Features.Orders;

/// <summary>
/// Turns a basket into an order.
///
/// <para>
/// The one place in the system where a price stops being a lookup and becomes a fact. Everything
/// the order will ever show — names, prices, the fee, the address, the rate — is copied here, so
/// a restaurant renaming a burger or raising a price next week cannot restate what somebody
/// bought today.
/// </para>
/// <para>
/// It refuses in more ways than anything else in the application, and each refusal names what is
/// wrong: "closed", "below the minimum", "sold out while you were deciding" and "the price moved"
/// are four different problems with four different things a person can do about them.
/// </para>
/// </summary>
public sealed class CheckoutService(
    IAppDbContext db,
    ITenantGuard guard,
    IValidationService validation,
    IClock clock,
    CartPricing pricing,
    IOrderNumberAllocator orderNumbers,
    IOrderNotifier notifier)
{
    public async Task<PlacedOrderResponse> CheckoutAsync(
        Guid restaurantId, CheckoutRequest request, CancellationToken ct = default)
    {
        await validation.ValidateAsync(request, ct);
        var userId = guard.RequireUserId();

        // The double-tap. Answered before anything else is checked, because by now the order
        // exists and questions like "is the restaurant still open" would refuse a customer for
        // an order they already successfully placed.
        var existing = await FindByKeyAsync(request.IdempotencyKey, userId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var restaurant = await LoadOpenRestaurantAsync(restaurantId, ct);

        var cart = await db.Carts
            .FirstOrDefaultAsync(c => c.UserId == userId && c.RestaurantId == restaurantId, ct)
            ?? throw new ConflictException("Your basket is empty.");

        var lines = await pricing.PriceLinesAsync(cart.Id, ct);
        if (lines.Count == 0)
        {
            throw new ConflictException("Your basket is empty.");
        }

        // A dish taken off the menu while the basket sat. Naming them is the difference between
        // a customer fixing it in one tap and wondering what went wrong.
        var missing = lines.Where(l => !l.IsAvailable).Select(l => l.Name).ToArray();
        if (missing.Length > 0)
        {
            throw new ConflictException(missing.Length == 1
                ? $"{missing[0]} is no longer available. Remove it to continue."
                : $"These are no longer available: {string.Join(", ", missing)}. Remove them to continue.");
        }

        var delivery = await pricing.ResolveDeliveryAsync(
            restaurantId, request.Fulfillment, request.AddressId, userId, ct);

        var price = OrderPricing.Calculate(new PricingInputs(
            Lines: [.. lines.Select(l => new PricedLine(l.UnitPriceUsd, l.Quantity))],
            DeliveryFeeUsd: delivery.FeeUsd,
            DiscountUsd: 0m,
            CommissionPercent: restaurant.CommissionPercent,
            PrepMinutes: restaurant.DefaultPrepMinutes,
            TravelMinutes: delivery.TravelMinutes,
            MinOrderUsd: restaurant.MinOrderUsd,
            ExchangeRateLbpPerUsd: await pricing.CurrentRateAsync(ct)));

        if (!price.MeetsMinimum)
        {
            throw new ConflictException(
                $"This restaurant's minimum order is ${price.MinOrderUsd:0.00}. "
                + $"Add ${price.ShortfallUsd:0.00} more to continue.");
        }

        // The price the customer agreed to, checked against the one just computed. The server's
        // figure is the one that counts; this only decides whether to go ahead with it.
        if (request.ExpectedTotalUsd != price.TotalUsd)
        {
            throw new ConflictException(
                $"The total changed while you were checking out — it is now ${price.TotalUsd:0.00}, "
                + $"not ${request.ExpectedTotalUsd:0.00}. Check your basket and try again.");
        }

        var address = await LoadAddressSnapshotAsync(request.AddressId, userId, ct);
        var order = await BuildOrderAsync(restaurantId, userId, request, price, delivery, address, ct);

        AddLines(order, lines);

        // The first entry in the trail. Every later move appends another, so an order can always
        // say who did what and when.
        db.OrderEvents.Add(new OrderEvent
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FromStatus = null,
            ToStatus = OrderStatus.Placed,
            ChangedByUserId = userId,
            CreatedAt = clock.UtcNow,
        });

        // The basket is gone the moment the order exists. Leaving it would let a second checkout
        // place the same food twice.
        db.CartLines.RemoveRange(
            await db.CartLines.Where(l => l.CartId == cart.Id).ToListAsync(ct));

        db.Orders.Add(order);

        // One SaveChanges, so the order, its lines, its first event and the emptied basket either
        // all happen or none of them do.
        await db.SaveChangesAsync(ct);

        // After the commit, never inside it. A message cannot be rolled back, so a kitchen told
        // about an order that then failed to save would be cooking food nobody ordered. The
        // notifier swallows its own failures for the mirror-image reason.
        await notifier.OrderChangedAsync(restaurantId, userId,
            new OrderChanged(order.Id, order.OrderNumber, order.Status, null, order.PlacedAt), ct);

        return new PlacedOrderResponse(
            order.Id,
            order.OrderNumber,
            order.Status,
            order.FulfillmentType,
            order.SubtotalUsd,
            order.DeliveryFeeUsd,
            order.TotalUsd,
            price.TotalLbp,
            order.PromisedMinutesMin,
            order.PromisedMinutesMax,
            order.PaymentMethod,
            order.PaymentStatus,
            order.PlacedAt);
    }

    // ------------------------------------------------------------------ the refusals

    /// <summary>
    /// The restaurant, but only if it can actually take this order right now.
    ///
    /// Three separate conditions with three separate answers: gone, deliberately paused, or
    /// simply shut for the night. "Closed" when the owner has paused orders would send somebody
    /// away to come back at opening time and find it still paused.
    /// </summary>
    private async Task<RestaurantForCheckout> LoadOpenRestaurantAsync(Guid restaurantId, CancellationToken ct)
    {
        var restaurant = await db.Restaurants.AsNoTracking()
            .Where(r => r.Id == restaurantId && r.IsActive)
            .Select(r => new RestaurantForCheckout(
                r.Name,
                r.Slug,
                r.CommissionPercent,
                r.MinOrderUsd,
                r.DefaultPrepMinutes,
                r.IsAcceptingOrders,
                r.Hours.Select(h => new RestaurantHours
                {
                    DayOfWeek = h.DayOfWeek,
                    OpenTime = h.OpenTime,
                    CloseTime = h.CloseTime,
                }).ToList()))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("That restaurant is not taking orders.");

        if (!restaurant.IsAcceptingOrders)
        {
            throw new ConflictException($"{restaurant.Name} has paused new orders. Try again shortly.");
        }

        // Wall-clock in the restaurant's own timezone, because a kitchen opens at eleven where it
        // stands and not at eleven UTC.
        var local = clock.LocalNow;
        if (!OpeningHours.IsOpenAt(restaurant.Hours, local.DayOfWeek, TimeOnly.FromDateTime(local.DateTime)))
        {
            throw new ConflictException($"{restaurant.Name} is closed right now.");
        }

        return restaurant;
    }

    private async Task<AddressSnapshot?> LoadAddressSnapshotAsync(
        Guid? addressId, Guid userId, CancellationToken ct)
    {
        if (addressId is null)
        {
            return null;
        }

        // Copied onto the order rather than referenced. The customer may edit or delete this
        // address tomorrow, and the courier still has to know where the food went today.
        return await db.Addresses.AsNoTracking()
            .Where(a => a.Id == addressId && a.UserId == userId)
            .Select(a => new AddressSnapshot(
                a.Id, a.Zone.Name, a.Line1, a.Building, a.Floor, a.Landmark, a.Lat, a.Lng))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("That address is not one of yours.");
    }

    // ------------------------------------------------------------------ building it

    private async Task<Order> BuildOrderAsync(
        Guid restaurantId,
        Guid userId,
        CheckoutRequest request,
        OrderPrice price,
        (decimal FeeUsd, int TravelMinutes, string? ZoneName) delivery,
        AddressSnapshot? address,
        CancellationToken ct)
    {
        var restaurant = await db.Restaurants.AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => r.Slug)
            .FirstAsync(ct);

        // The restaurant's own calendar day, so a number reads as the date a kitchen worked.
        var businessDate = clock.LocalToday;
        var sequence = await orderNumbers.NextAsync(restaurantId, businessDate, ct);

        return new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = OrderNumbers.Format(restaurant, businessDate, sequence),
            CustomerId = userId,
            RestaurantId = restaurantId,
            AddressId = address?.Id,
            FulfillmentType = request.Fulfillment,
            Status = OrderStatus.Placed,

            DeliveryZoneName = delivery.ZoneName,
            DeliveryLine1 = address?.Line1,
            DeliveryBuilding = address?.Building,
            DeliveryFloor = address?.Floor,
            DeliveryLandmark = address?.Landmark,
            DeliveryLat = address?.Lat,
            DeliveryLng = address?.Lng,

            PaymentMethod = request.PaymentMethod,
            // Nothing has been collected yet, whichever method was chosen. Cash is pending until
            // the courier is handed it, and the online gateway is Phase 4.
            PaymentStatus = PaymentStatus.Pending,

            SubtotalUsd = price.SubtotalUsd,
            DeliveryFeeUsd = price.DeliveryFeeUsd,
            TaxUsd = price.TaxUsd,
            DiscountUsd = price.DiscountUsd,
            TotalUsd = price.TotalUsd,
            ExchangeRateLbp = price.TotalLbp is null || price.TotalUsd == 0m
                ? await pricing.CurrentRateAsync(ct) ?? 0m
                : decimal.Round(price.TotalLbp.Value / price.TotalUsd, 4),

            CommissionPercent = price.CommissionPercent,
            CommissionUsd = price.CommissionUsd,

            PromisedMinutesMin = price.PromisedMinutesMin,
            PromisedMinutesMax = price.PromisedMinutesMax,

            CustomerNote = string.IsNullOrWhiteSpace(request.CustomerNote)
                ? null
                : request.CustomerNote.Trim(),

            IdempotencyKey = request.IdempotencyKey,

            PlacedAt = clock.UtcNow,
        };
    }

    /// <summary>
    /// An order already placed under this key, if there is one.
    ///
    /// Scoped to the caller's own orders. Keys are client-generated, and answering for anybody's
    /// would turn the endpoint into a way to read a stranger's order by guessing.
    /// </summary>
    private async Task<PlacedOrderResponse?> FindByKeyAsync(
        Guid key, Guid userId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.IdempotencyKey == key && o.CustomerId == userId)
            .FirstOrDefaultAsync(ct);

        if (order is null)
        {
            return null;
        }

        return new PlacedOrderResponse(
            order.Id, order.OrderNumber, order.Status, order.FulfillmentType,
            order.SubtotalUsd, order.DeliveryFeeUsd, order.TotalUsd,
            order.ExchangeRateLbp == 0m
                ? null
                : decimal.Round(order.TotalUsd * order.ExchangeRateLbp, 0, MidpointRounding.AwayFromZero),
            order.PromisedMinutesMin, order.PromisedMinutesMax,
            order.PaymentMethod, order.PaymentStatus, order.PlacedAt);
    }

    /// <summary>
    /// Copies the basket onto the order. Names and prices become text and numbers here, never
    /// lookups, which is what lets a receipt still read correctly after the menu moves on.
    /// </summary>
    private static void AddLines(Order order, IReadOnlyList<CartLineResponse> lines)
    {
        foreach (var line in lines)
        {
            var orderLine = new OrderLine
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                MenuItemId = line.MenuItemId,
                ItemNameSnapshot = line.Name,
                UnitPriceUsd = line.UnitPriceUsd,
                Quantity = line.Quantity,
                LineTotalUsd = line.LineTotalUsd,
                Note = line.Note,
            };

            foreach (var option in line.Options)
            {
                orderLine.SelectedOptions.Add(new OrderLineOption
                {
                    Id = Guid.NewGuid(),
                    OrderLineId = orderLine.Id,
                    OptionId = option.OptionId,
                    GroupNameSnapshot = option.GroupName,
                    OptionNameSnapshot = option.Name,
                    PriceDeltaUsd = option.PriceDeltaUsd,
                    Quantity = option.Quantity,
                });
            }

            order.Lines.Add(orderLine);
        }
    }

    private sealed record RestaurantForCheckout(
        string Name,
        string Slug,
        decimal CommissionPercent,
        decimal MinOrderUsd,
        int DefaultPrepMinutes,
        bool IsAcceptingOrders,
        List<RestaurantHours> Hours);

    private sealed record AddressSnapshot(
        Guid Id, string ZoneName, string Line1, string? Building, string? Floor,
        string? Landmark, decimal? Lat, decimal? Lng);
}
