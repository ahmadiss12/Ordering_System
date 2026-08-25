using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Exceptions;

namespace OrderingSystem.Application.Features.Catalogue;

/// <summary>
/// The public storefront reads: the restaurant list, one restaurant, and its menu.
/// <para>
/// Every query here projects straight into a DTO inside the <c>Select</c> — see ADR-09. On the
/// menu, the most-hit read in the system, that is the difference between fetching the columns the
/// response needs and hydrating whole entity graphs to throw most of them away.
/// </para>
/// <para>
/// Nothing in this file filters by tenant, and that is correct rather than an omission. Menus,
/// categories, options, opening hours and zone fees carry no tenant query filter: on a
/// marketplace they are public by design. Isolation on the catalogue is a <em>write</em> concern,
/// enforced by ownership checks in the staff endpoints that edit it.
/// </para>
/// </summary>
public sealed class CatalogueService(IAppDbContext db)
{
    public async Task<IReadOnlyList<RestaurantSummaryResponse>> ListRestaurantsAsync(
        CancellationToken ct = default) =>
        await db.Restaurants
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .Select(r => new RestaurantSummaryResponse(
                r.Id,
                r.Name,
                r.Slug,
                r.Description,
                r.LogoUrl,
                r.CoverUrl,
                r.MinOrderUsd,
                r.DefaultPrepMinutes,
                r.IsAcceptingOrders))
            .ToListAsync(ct);

    public async Task<RestaurantDetailResponse> GetRestaurantAsync(
        string slug, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(slug);
        var wanted = Normalise(slug);

        var restaurant = await db.Restaurants
            .AsNoTracking()
            .Where(r => r.IsActive && r.Slug == wanted)
            .Select(r => new RestaurantDetailResponse(
                r.Id,
                r.Name,
                r.Slug,
                r.Description,
                r.LogoUrl,
                r.CoverUrl,
                r.Phone,
                r.MinOrderUsd,
                r.DefaultPrepMinutes,
                r.IsAcceptingOrders,
                r.Hours
                    .OrderBy(h => h.DayOfWeek)
                    .ThenBy(h => h.OpenTime)
                    .Select(h => new OpeningWindowResponse(h.DayOfWeek, h.OpenTime, h.CloseTime))
                    .ToList(),
                // A zone is offered only if both the restaurant's row and the platform zone are
                // live, so a platform admin retiring a zone removes it everywhere at once.
                r.Zones
                    .Where(z => z.IsActive && z.Zone.IsActive)
                    .OrderBy(z => z.Zone.Name)
                    .Select(z => new DeliveryZoneFeeResponse(
                        z.ZoneId, z.Zone.Name, z.DeliveryFeeUsd, z.EstimatedMinutes))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        return restaurant ?? throw new NotFoundException($"No restaurant with slug '{slug}'.");
    }

    public async Task<MenuResponse> GetMenuAsync(string slug, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(slug);
        var wanted = Normalise(slug);

        // Note what is absent: no "where not deleted" on items, groups or options. The global
        // query filters apply to navigations inside a projection too, so soft-deleted rows are
        // already gone. The one explicit check below is a different case, and is commented there.
        var menu = await db.Restaurants
            .AsNoTracking()
            .Where(r => r.IsActive && r.Slug == wanted)
            // Slug is unique, so this never changes which row comes back. It is here because a
            // split query combined with a row-limiting operator and no ORDER BY is undefined
            // across the separate queries, and EF warns about exactly that.
            .OrderBy(r => r.Slug)
            .Select(r => new MenuResponse(
                r.Id,
                r.Slug,
                r.Categories
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.SortOrder)
                    .ThenBy(c => c.Name)
                    .Select(c => new MenuCategoryResponse(
                        c.Id,
                        c.Name,
                        c.SortOrder,
                        c.MenuItems
                            .OrderBy(i => i.SortOrder)
                            .ThenBy(i => i.Name)
                            .Select(i => new MenuItemResponse(
                                i.Id,
                                i.Name,
                                i.Description,
                                i.BasePriceUsd,
                                i.ImageUrl,
                                i.IsAvailable,
                                i.SortOrder,
                                i.OptionGroups
                                    // OptionGroup is a required navigation carrying its own soft
                                    // -delete filter. A deleted group would leave this join row
                                    // pointing at nothing and the projection would read a name
                                    // off null, so the join row is dropped explicitly.
                                    .Where(g => !g.OptionGroup.IsDeleted)
                                    .OrderBy(g => g.SortOrder)
                                    .Select(g => new MenuOptionGroupResponse(
                                        g.OptionGroupId,
                                        g.OptionGroup.Name,
                                        // The client is told the bounds that apply to THIS item.
                                        // Translated to COALESCE, so the resolution happens in
                                        // SQL rather than over hydrated entities.
                                        g.MinSelectOverride ?? g.OptionGroup.MinSelect,
                                        g.MaxSelectOverride ?? g.OptionGroup.MaxSelect,
                                        g.SortOrder,
                                        g.OptionGroup.Options
                                            .OrderBy(o => o.SortOrder)
                                            .ThenBy(o => o.Name)
                                            .Select(o => new MenuOptionResponse(
                                                o.Id,
                                                o.Name,
                                                o.PriceDeltaUsd,
                                                o.MaxQuantity,
                                                o.IsAvailable,
                                                o.SortOrder))
                                            .ToList()))
                                    .ToList()))
                            .ToList()))
                    .ToList()))
            // Four levels of nested collections in one SELECT is a cartesian product: every
            // option row repeated for every sibling item. Split into one query per level instead.
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct);

        return menu ?? throw new NotFoundException($"No restaurant with slug '{slug}'.");
    }

    /// <summary>Slugs are lower-case by construction, but a link that arrives shouting should still resolve.</summary>
    private static string Normalise(string slug) => slug.Trim().ToLowerInvariant();
}
