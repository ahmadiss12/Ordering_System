using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Exceptions;

namespace OrderingSystem.Application.Features.Restaurants;

/// <summary>
/// What a restaurant can change about itself.
///
/// <para>
/// Every write here goes through <see cref="ITenantGuard.RequireRestaurantId"/> rather than taking
/// a restaurant id from the caller. That is ADR-07's explicit half: a query filter is a WHERE
/// clause and an UPDATE that trusted an id in the URL would happily edit somebody else's
/// restaurant. The id never leaves the token.
/// </para>
/// <para>
/// Nothing here touches commission or the active switch. They are on the response because a
/// restaurant is entitled to see what it is being charged and whether the platform has switched
/// it off; they are absent from the request because neither is theirs to set.
/// </para>
/// </summary>
public sealed class RestaurantSettingsService(
    IAppDbContext db, ITenantGuard guard, IValidationService validation)
{
    public async Task<RestaurantSettingsResponse> GetAsync(CancellationToken ct = default)
    {
        var restaurantId = guard.RequireRestaurantId();

        return await db.Restaurants.AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => new RestaurantSettingsResponse(
                r.Id, r.Name, r.Slug, r.Description, r.Phone,
                r.DefaultPrepMinutes, r.MinOrderUsd,
                r.IsAcceptingOrders, r.IsActive, r.CommissionPercent))
            .FirstOrDefaultAsync(ct)
            // Only reachable if a token names a restaurant that has since been deleted, which
            // nothing does today. Saying so beats a NullReferenceException in a log.
            ?? throw new NotFoundException("That restaurant no longer exists.");
    }

    public async Task<RestaurantSettingsResponse> UpdateAsync(
        UpdateRestaurantSettingsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        var restaurant = await LoadForWriteAsync(ct);

        restaurant.Name = request.Name.Trim();
        restaurant.Description = Blank(request.Description);
        restaurant.Phone = request.Phone.Trim();
        restaurant.DefaultPrepMinutes = request.DefaultPrepMinutes;

        // Changing this affects the next order and no earlier one. Every order snapshots the
        // minimum it was judged against, along with its prices, its fee and the exchange rate —
        // which is what stops an edit here restating what somebody was charged last night.
        restaurant.MinOrderUsd = request.MinOrderUsd;

        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    /// <summary>
    /// The rush switch, on its own so staff can reach it. Separate from opening hours because it
    /// answers a different question: the hours say when a kitchen intends to be open, this says
    /// whether it can cope right now.
    /// </summary>
    public async Task<RestaurantSettingsResponse> SetAcceptingOrdersAsync(
        bool isAcceptingOrders, CancellationToken ct = default)
    {
        var restaurant = await LoadForWriteAsync(ct);

        restaurant.IsAcceptingOrders = isAcceptingOrders;
        await db.SaveChangesAsync(ct);

        return await GetAsync(ct);
    }

    private async Task<Domain.Restaurants.Restaurant> LoadForWriteAsync(CancellationToken ct)
    {
        var restaurantId = guard.RequireRestaurantId();

        return await db.Restaurants.FirstOrDefaultAsync(r => r.Id == restaurantId, ct)
            ?? throw new NotFoundException("That restaurant no longer exists.");
    }

    /// <summary>Empty and whitespace both mean "not set", so they are stored the same way.</summary>
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
