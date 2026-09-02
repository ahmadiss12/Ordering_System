using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Application.Features.Cart;

/// <summary>
/// The reads a basket needs pricing, shared by the cart screen, the quote and checkout.
///
/// <para>
/// One component rather than three copies. Two implementations of "what does this line cost" is
/// exactly how a basket badge, a quote and the amount finally charged come to disagree — and the
/// last of those is the one somebody notices on their bank statement.
/// </para>
/// </summary>
public sealed class CartPricing(IAppDbContext db, IClock clock)
{
    /// <summary>
    /// The lines as they stand, priced from today's menu.
    ///
    /// <para>
    /// One place, used by both the cart response and the quote. Two implementations of "what does
    /// this line cost" is exactly how a basket badge and a checkout total come to disagree.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<CartLineResponse>> PriceLinesAsync(Guid cartId, CancellationToken ct)
    {
        var stored = await db.CartLines.AsNoTracking()
            .Where(l => l.CartId == cartId)
            .Select(l => new { l.Id, l.MenuItemId, l.Quantity, l.Note })
            .ToListAsync(ct);

        var lineIds = stored.Select(l => l.Id).ToArray();
        var itemIds = stored.Select(l => l.MenuItemId).Distinct().ToArray();

        var items = await db.MenuItems.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Name, i.ImageUrl, i.BasePriceUsd, i.IsAvailable })
            .ToDictionaryAsync(i => i.Id, ct);

        var options = await db.CartLineOptions.AsNoTracking()
            .Where(o => lineIds.Contains(o.CartLineId))
            .Select(o => new
            {
                o.CartLineId,
                o.OptionId,
                o.Quantity,
                o.Option.Name,
                GroupName = o.Option.OptionGroup.Name,
                o.Option.PriceDeltaUsd,
                o.Option.SortOrder,
            })
            .ToListAsync(ct);

        var lines = new List<CartLineResponse>(stored.Count);

        foreach (var line in stored.OrderBy(l => l.Id))
        {
            // A dish deleted from the menu while it sat in a basket. Shown as unavailable rather
            // than dropped, so the customer knows why their total changed.
            var item = items.GetValueOrDefault(line.MenuItemId);

            var chosen = options
                .Where(o => o.CartLineId == line.Id)
                .OrderBy(o => o.SortOrder)
                .ToList();

            var unitPrice = OrderPricing.Round(
                (item?.BasePriceUsd ?? 0m) + chosen.Sum(o => o.PriceDeltaUsd * o.Quantity));

            lines.Add(new CartLineResponse(
                line.Id,
                line.MenuItemId,
                item?.Name ?? "No longer on the menu",
                item?.ImageUrl,
                line.Quantity,
                line.Note,
                item?.IsAvailable ?? false,
                unitPrice,
                unitPrice * line.Quantity,
                [.. chosen.Select(o => new CartLineOptionResponse(
                    o.OptionId, o.GroupName, o.Name, o.Quantity, o.PriceDeltaUsd))]));
        }

        return lines;
    }

    /// <summary>
    /// The fee and travel time for this order, or a refusal a person can act on.
    ///
    /// Pickup costs nothing and travels nowhere. Delivery needs an address that is the caller's,
    /// in a zone this restaurant actually serves — and "we do not deliver there" is a different
    /// answer from "that address does not exist", so they are not collapsed.
    /// </summary>
    public async Task<(decimal FeeUsd, int TravelMinutes, string? ZoneName)> ResolveDeliveryAsync(
        Guid restaurantId, FulfillmentType fulfillment, Guid? addressId, Guid userId, CancellationToken ct)
    {
        if (fulfillment == FulfillmentType.Pickup)
        {
            return (0m, 0, null);
        }

        if (addressId is null)
        {
            throw new ValidationFailedException(
                "A delivery order needs an address.",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["addressId"] = ["Choose where the order should go."],
                });
        }

        var address = await db.Addresses.AsNoTracking()
            .Where(a => a.Id == addressId && a.UserId == userId)
            .Select(a => new { a.ZoneId, ZoneName = a.Zone.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("That address is not one of yours.");

        var zone = await db.RestaurantZones.AsNoTracking()
            .Where(z => z.RestaurantId == restaurantId && z.ZoneId == address.ZoneId && z.IsActive)
            .Select(z => new { z.DeliveryFeeUsd, z.EstimatedMinutes })
            .FirstOrDefaultAsync(ct)
            ?? throw new ConflictException($"This restaurant does not deliver to {address.ZoneName}.");

        return (zone.DeliveryFeeUsd, zone.EstimatedMinutes, address.ZoneName);
    }

    /// <summary>
    /// The rate in force now. Null when none has been set — the quote is still correct in
    /// dollars, and a made-up rate would be worse than no pound figure at all.
    /// </summary>
    public async Task<decimal?> CurrentRateAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;

        return await db.ExchangeRates.AsNoTracking()
            .Where(r => r.EffectiveFrom <= now)
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => (decimal?)r.RateLbpPerUsd)
            .FirstOrDefaultAsync(ct);
    }
}
