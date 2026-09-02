using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Carts;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Menu;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Application.Features.Cart;

/// <summary>
/// A customer's basket, one per restaurant.
///
/// <para>
/// Carts carry a query filter on <c>UserId</c>, so a read cannot return somebody else's. Writes
/// have no WHERE clause, which is exactly the hole ADR-07 describes — every method here loads
/// the row and checks the owner before touching it, rather than trusting the filter to have done
/// it.
/// </para>
/// <para>
/// Nothing is priced into the cart. Prices are read from the menu on every view, so a basket
/// left open overnight shows today's numbers; freezing them happens once, at checkout.
/// </para>
/// </summary>
public sealed class CartService(
    IAppDbContext db,
    ITenantGuard guard,
    IValidationService validation,
    IClock clock,
    CartPricing pricing)
{
    /// <summary>A cap on one line, so a typo cannot become a thousand burgers.</summary>
    public const int MaxLineQuantity = 99;

    public async Task<CartResponse> GetAsync(Guid restaurantId, CancellationToken ct = default)
    {
        var userId = guard.RequireUserId();
        var cart = await LoadAsync(userId, restaurantId, ct);

        return cart is null
            ? await EmptyAsync(restaurantId, ct)
            : await ProjectAsync(cart.Id, restaurantId, ct);
    }

    public async Task<CartResponse> AddLineAsync(
        Guid restaurantId, AddCartLineRequest request, CancellationToken ct = default)
    {
        await validation.ValidateAsync(request, ct);
        var userId = guard.RequireUserId();

        var item = await db.MenuItems.AsNoTracking()
            .Where(i => i.Id == request.MenuItemId)
            .Select(i => new { i.Id, i.RestaurantId, i.Name, i.IsAvailable })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("That item is no longer on the menu.");

        // Ordering across restaurants in one basket would produce an order no single kitchen
        // could fulfil, so the item has to belong to the restaurant the cart is for.
        if (item.RestaurantId != restaurantId)
        {
            throw new ConflictException("That item belongs to a different restaurant.");
        }

        if (!item.IsAvailable)
        {
            throw new ConflictException($"{item.Name} is not available right now.");
        }

        await EnsureSelectionIsValidAsync(request.MenuItemId, request.Options, ct);

        var cart = await LoadAsync(userId, restaurantId, ct) ?? await CreateAsync(userId, restaurantId, ct);

        // The same dish with the same options is the same line. Adding it again means "one more
        // of those", not a second row saying the same thing.
        var existing = cart.Lines.FirstOrDefault(line =>
            line.MenuItemId == request.MenuItemId
            && line.Note == Normalise(request.Note)
            && SameOptions(line, request.Options));

        if (existing is not null)
        {
            existing.Quantity = Math.Min(existing.Quantity + request.Quantity, MaxLineQuantity);
        }
        else
        {
            var line = new CartLine
            {
                Id = Guid.NewGuid(),
                CartId = cart.Id,
                MenuItemId = request.MenuItemId,
                Quantity = request.Quantity,
                Note = Normalise(request.Note),
            };

            foreach (var option in request.Options)
            {
                line.SelectedOptions.Add(new CartLineOption
                {
                    CartLineId = line.Id,
                    OptionId = option.OptionId,
                    Quantity = option.Quantity,
                });
            }

            // Added through the DbSet, not through cart.Lines.
            //
            // EF decides the state of an entity it discovers through a navigation by whether the
            // key is already set. CartLine's Guid key is ValueGeneratedOnAdd, so a line arriving
            // with an Id of our own making looks like a row that already exists: EF marked it
            // Modified and the UPDATE hit nothing. It only worked while the cart itself was new,
            // because children of an Added root are Added too.
            db.CartLines.Add(line);
        }

        cart.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return await ProjectAsync(cart.Id, restaurantId, ct);
    }

    public async Task<CartResponse> UpdateLineAsync(
        Guid lineId, UpdateCartLineRequest request, CancellationToken ct = default)
    {
        await validation.ValidateAsync(request, ct);

        var line = await LoadOwnedLineAsync(lineId, ct);

        line.Quantity = request.Quantity;
        line.Note = Normalise(request.Note);
        line.Cart.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        return await ProjectAsync(line.CartId, line.Cart.RestaurantId, ct);
    }

    public async Task<CartResponse> RemoveLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await LoadOwnedLineAsync(lineId, ct);
        var cart = line.Cart;

        db.CartLines.Remove(line);
        cart.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return await ProjectAsync(cart.Id, cart.RestaurantId, ct);
    }

    /// <summary>Empties the basket but keeps it, so the next add does not have to create one.</summary>
    public async Task<CartResponse> ClearAsync(Guid restaurantId, CancellationToken ct = default)
    {
        var userId = guard.RequireUserId();
        var cart = await LoadAsync(userId, restaurantId, ct);

        if (cart is null)
        {
            return await EmptyAsync(restaurantId, ct);
        }

        db.CartLines.RemoveRange(cart.Lines);
        cart.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return await ProjectAsync(cart.Id, restaurantId, ct);
    }

    // ------------------------------------------------------------------ what it would cost

    /// <summary>
    /// Prices the basket without committing to anything.
    ///
    /// <para>
    /// The same figures checkout will store, produced by the same calculation, so the number a
    /// customer agrees to is the number they are charged. Nothing here writes.
    /// </para>
    /// </summary>
    public async Task<QuoteResponse> QuoteAsync(
        Guid restaurantId, FulfillmentType fulfillment, Guid? addressId, CancellationToken ct = default)
    {
        var userId = guard.RequireUserId();

        var restaurant = await db.Restaurants.AsNoTracking()
            .Where(r => r.Id == restaurantId && r.IsActive)
            .Select(r => new { r.CommissionPercent, r.MinOrderUsd, r.DefaultPrepMinutes })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("That restaurant is not taking orders.");

        var cart = await db.Carts.AsNoTracking()
            .Where(c => c.UserId == userId && c.RestaurantId == restaurantId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        var lines = cart is null ? [] : await pricing.PriceLinesAsync(cart.Value, ct);

        // Only what can actually be ordered. Pricing a sold-out dish would quote a total the
        // customer will not be charged.
        var orderable = lines.Where(l => l.IsAvailable).ToList();

        var delivery = await pricing.ResolveDeliveryAsync(restaurantId, fulfillment, addressId, userId, ct);

        var price = OrderPricing.Calculate(new PricingInputs(
            Lines: [.. orderable.Select(l => new PricedLine(l.UnitPriceUsd, l.Quantity))],
            DeliveryFeeUsd: delivery.FeeUsd,
            // No promo codes are in scope, so nothing can produce one yet. It is carried through
            // the calculation rather than assumed away, so adding them later is one caller change.
            DiscountUsd: 0m,
            CommissionPercent: restaurant.CommissionPercent,
            PrepMinutes: restaurant.DefaultPrepMinutes,
            TravelMinutes: delivery.TravelMinutes,
            MinOrderUsd: restaurant.MinOrderUsd,
            ExchangeRateLbpPerUsd: await pricing.CurrentRateAsync(ct)));

        return new QuoteResponse(
            restaurantId,
            fulfillment,
            orderable.Sum(l => l.Quantity),
            price.SubtotalUsd,
            price.DeliveryFeeUsd,
            price.TaxUsd,
            price.DiscountUsd,
            price.TotalUsd,
            price.TotalLbp,
            price.PromisedMinutesMin,
            price.PromisedMinutesMax,
            price.MinOrderUsd,
            price.MeetsMinimum,
            price.ShortfallUsd,
            lines.Any(l => !l.IsAvailable),
            delivery.ZoneName);
    }

    // ------------------------------------------------------------------ rules

    /// <summary>
    /// Checks the chosen options against what the item actually offers, with any per-item
    /// override already applied — the same resolution the customer saw on the menu.
    /// </summary>
    private async Task EnsureSelectionIsValidAsync(
        Guid menuItemId, IReadOnlyList<ChosenOptionRequest> chosen, CancellationToken ct)
    {
        var groups = await db.MenuItemOptionGroups.AsNoTracking()
            .Where(link => link.MenuItemId == menuItemId)
            .Select(link => new GroupBounds(
                link.OptionGroup.Id,
                link.OptionGroup.Name,
                link.MinSelectOverride ?? link.OptionGroup.MinSelect,
                link.MaxSelectOverride ?? link.OptionGroup.MaxSelect))
            .ToListAsync(ct);

        var ids = chosen.Select(o => o.OptionId).ToArray();

        var known = await db.Options.AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.OptionGroupId, o.Name, o.MaxQuantity, o.IsAvailable })
            .ToDictionaryAsync(o => o.Id, ct);

        var picked = new List<PickedOption>(chosen.Count);
        foreach (var option in chosen)
        {
            // An id nothing matches cannot be described, so it is reported before the rules run
            // rather than being silently dropped from them.
            if (!known.TryGetValue(option.OptionId, out var row))
            {
                throw new ValidationFailedException(
                    "One of the choices does not exist.",
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["options"] = ["A choice on this item is no longer on the menu."],
                    });
            }

            picked.Add(new PickedOption(
                row.Id, row.OptionGroupId, row.Name, option.Quantity, row.MaxQuantity, row.IsAvailable));
        }

        var errors = OptionSelection.Validate(groups, picked);
        if (errors.Count == 0)
        {
            return;
        }

        throw new ValidationFailedException(
            "That combination of choices is not available.",
            errors
                .GroupBy(e => e.Field, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray(), StringComparer.Ordinal));
    }

    // ------------------------------------------------------------------ loading

    private Task<Domain.Carts.Cart?> LoadAsync(Guid userId, Guid restaurantId, CancellationToken ct) =>
        db.Carts
            .Include(c => c.Lines)
            .ThenInclude(l => l.SelectedOptions)
            .FirstOrDefaultAsync(c => c.UserId == userId && c.RestaurantId == restaurantId, ct);

    private async Task<Domain.Carts.Cart> CreateAsync(Guid userId, Guid restaurantId, CancellationToken ct)
    {
        var exists = await db.Restaurants.AsNoTracking().AnyAsync(r => r.Id == restaurantId && r.IsActive, ct);
        if (!exists)
        {
            throw new NotFoundException("That restaurant is not taking orders.");
        }

        var cart = new Domain.Carts.Cart
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RestaurantId = restaurantId,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };

        db.Carts.Add(cart);
        return cart;
    }

    /// <summary>
    /// A line, but only if it is the caller's.
    ///
    /// The query filter would already hide another customer's line from a read, so this mostly
    /// re-states it — but a filter is a WHERE clause, and the day somebody writes a query that
    /// bypasses it, this is the check that still stands.
    /// </summary>
    private async Task<CartLine> LoadOwnedLineAsync(Guid lineId, CancellationToken ct)
    {
        var userId = guard.RequireUserId();

        var line = await db.CartLines
            .Include(l => l.Cart)
            .ThenInclude(c => c.Lines)
            .ThenInclude(l => l.SelectedOptions)
            .FirstOrDefaultAsync(l => l.Id == lineId, ct)
            ?? throw new NotFoundException("That item is not in your basket.");

        if (line.Cart.UserId != userId)
        {
            // Not found rather than forbidden: confirming the line exists would tell a stranger
            // something about somebody else's basket.
            throw new NotFoundException("That item is not in your basket.");
        }

        return line;
    }

    // ------------------------------------------------------------------ projection

    private async Task<CartResponse> EmptyAsync(Guid restaurantId, CancellationToken ct)
    {
        var restaurant = await db.Restaurants.AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => new { r.Name, r.Slug })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("No such restaurant.");

        return new CartResponse(
            Guid.Empty, restaurantId, restaurant.Name, restaurant.Slug, 0, 0m, false, []);
    }

    /// <summary>
    /// Reads the cart back from the database with today's prices.
    ///
    /// <para>
    /// Deliberately re-queries rather than projecting the tracked graph. Two reasons: the menu
    /// data — names, prices, availability — is exactly what changes underneath a basket, and a
    /// navigation collection still holds rows that were just deleted, which is how emptying a
    /// cart came back reporting the lines it had removed.
    /// </para>
    /// </summary>
    private async Task<CartResponse> ProjectAsync(Guid cartId, Guid restaurantId, CancellationToken ct)
    {
        var restaurant = await db.Restaurants.AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => new { r.Name, r.Slug })
            .FirstAsync(ct);

        var lines = await pricing.PriceLinesAsync(cartId, ct);

        return new CartResponse(
            cartId,
            restaurantId,
            restaurant.Name,
            restaurant.Slug,
            lines.Sum(l => l.Quantity),
            // Only what can actually be ordered. Counting a sold-out dish would show a total the
            // customer will not be charged.
            OrderPricing.Round(lines.Where(l => l.IsAvailable).Sum(l => l.LineTotalUsd)),
            lines.Any(l => !l.IsAvailable),
            lines);
    }

    private static bool SameOptions(CartLine line, IReadOnlyList<ChosenOptionRequest> options) =>
        line.SelectedOptions.Count == options.Count
        && line.SelectedOptions.All(existing =>
            options.Any(o => o.OptionId == existing.OptionId && o.Quantity == existing.Quantity));

    private static string? Normalise(string? note)
    {
        var trimmed = note?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
