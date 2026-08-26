using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Application.Common;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Application.Features.Catalog;

/// <summary>
/// Everything an unauthenticated visitor can read: which restaurants exist, what they sell, and
/// what a given dish costs with its options.
/// <para>
/// These carry no tenant filter by design. On a marketplace the catalogue is public — filtering it
/// by the caller's restaurant would blank the storefront for any staff member who also orders as a
/// customer. Isolation on the catalogue is a write concern, enforced in <c>MenuAdminService</c>.
/// </para>
/// <para>
/// Every read here projects straight into a DTO rather than loading entities. The menu is the
/// most-requested query in the system, and the difference is selecting the columns a screen shows
/// versus hydrating an object graph to throw most of it away.
/// </para>
/// </summary>
public sealed class CatalogService(IAppDbContext db, IClock clock)
{
    public async Task<PagedResult<RestaurantSummary>> ListRestaurantsAsync(
        Guid? zoneId, int? page, int? pageSize, CancellationToken ct = default)
    {
        var (currentPage, size) = Paging.Normalise(page, pageSize);

        var query = db.Restaurants.AsNoTracking().Where(r => r.IsActive);

        // A zone turns the list into "who delivers to me", which is the question a customer
        // actually has. Without one they see everyone.
        if (zoneId is not null)
        {
            query = query.Where(r => r.Zones.Any(z => z.ZoneId == zoneId && z.IsActive));
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderBy(r => r.Name)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(r => new
            {
                r.Id, r.Name, r.Slug, r.Description, r.LogoUrl,
                r.MinOrderUsd, r.DefaultPrepMinutes, r.IsAcceptingOrders,
                Hours = r.Hours.Select(h => new { h.DayOfWeek, h.OpenTime, h.CloseTime }).ToList(),
                Zone = r.Zones
                    .Where(z => zoneId != null && z.ZoneId == zoneId && z.IsActive)
                    .Select(z => new { z.DeliveryFeeUsd, z.EstimatedMinutes })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new RestaurantSummary(
            r.Id, r.Name, r.Slug, r.Description, r.LogoUrl,
            r.MinOrderUsd, r.DefaultPrepMinutes, r.IsAcceptingOrders,
            IsOpenNow(r.Hours.Select(h => new RestaurantHours
            {
                DayOfWeek = h.DayOfWeek, OpenTime = h.OpenTime, CloseTime = h.CloseTime,
            })),
            r.Zone?.DeliveryFeeUsd,
            r.Zone?.EstimatedMinutes)).ToList();

        return new PagedResult<RestaurantSummary>(items, currentPage, size, total);
    }

    public async Task<RestaurantDetail> GetRestaurantAsync(string slug, CancellationToken ct = default)
    {
        var row = await db.Restaurants.AsNoTracking()
            .Where(r => r.Slug == slug && r.IsActive)
            .Select(r => new
            {
                r.Id, r.Name, r.Slug, r.Description, r.LogoUrl, r.CoverUrl, r.Phone,
                r.MinOrderUsd, r.DefaultPrepMinutes, r.IsAcceptingOrders,
                Hours = r.Hours.OrderBy(h => h.DayOfWeek).ThenBy(h => h.OpenTime)
                    .Select(h => new { h.DayOfWeek, h.OpenTime, h.CloseTime }).ToList(),
                Zones = r.Zones.Where(z => z.IsActive)
                    .Select(z => new ZoneDelivery(z.ZoneId, z.Zone.Name, z.DeliveryFeeUsd, z.EstimatedMinutes))
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"No restaurant found at '{slug}'.");

        var hours = row.Hours
            .Select(h => new RestaurantHours { DayOfWeek = h.DayOfWeek, OpenTime = h.OpenTime, CloseTime = h.CloseTime })
            .ToList();

        return new RestaurantDetail(
            row.Id, row.Name, row.Slug, row.Description, row.LogoUrl, row.CoverUrl, row.Phone,
            row.MinOrderUsd, row.DefaultPrepMinutes, row.IsAcceptingOrders,
            IsOpenNow(hours),
            [.. hours.Select(h => new OpeningWindow(h.DayOfWeek, h.OpenTime, h.CloseTime))],
            row.Zones);
    }

    /// <summary>
    /// A whole menu in one round trip. Deliberately not paged: a menu is tens of items, and a
    /// screen that shows all of them should not need three requests to do it.
    /// </summary>
    [SuppressMessage("Performance", "CA1860:Prefer comparing Count to 0 rather than using Any()",
        Justification = "This runs inside an IQueryable projection, where Any() becomes SQL EXISTS. "
                      + "The suggested Count comparison would emit COUNT(*), which is strictly more work.")]
    public async Task<RestaurantMenu> GetMenuAsync(string slug, CancellationToken ct = default)
    {
        return await db.Restaurants.AsNoTracking()
            .Where(r => r.Slug == slug && r.IsActive)
            .Select(r => new RestaurantMenu(
                r.Id,
                r.Name,
                r.Slug,
                r.Categories
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.SortOrder)
                    .Select(c => new MenuCategory(
                        c.Id,
                        c.Name,
                        c.SortOrder,
                        c.MenuItems
                            .OrderBy(i => i.SortOrder)
                            .Select(i => new MenuItemSummary(
                                i.Id, i.Name, i.Description, i.BasePriceUsd, i.ImageUrl,
                                i.IsAvailable, i.SortOrder,
                                i.OptionGroups.Any()))
                            .ToList()))
                    .ToList()))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"No restaurant found at '{slug}'.");

        // Soft-deleted items, groups and options are excluded by the global query filters rather
        // than by conditions written here - see AppDbContext.ApplyQueryFilters.
    }

    public async Task<MenuItemDetail> GetMenuItemAsync(Guid id, CancellationToken ct = default)
    {
        return await db.MenuItems.AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new MenuItemDetail(
                i.Id, i.RestaurantId, i.CategoryId, i.Name, i.Description,
                i.BasePriceUsd, i.ImageUrl, i.IsAvailable,
                i.OptionGroups
                    .OrderBy(link => link.SortOrder)
                    .Select(link => new ItemOptionGroup(
                        link.OptionGroup.Id,
                        link.OptionGroup.Name,
                        // The override is resolved here, in SQL. The client is told what applies
                        // to this item, never handed a group default and an override to reconcile.
                        link.MinSelectOverride ?? link.OptionGroup.MinSelect,
                        link.MaxSelectOverride ?? link.OptionGroup.MaxSelect,
                        link.SortOrder,
                        link.OptionGroup.Options
                            .OrderBy(o => o.SortOrder)
                            .Select(o => new ItemOption(
                                o.Id, o.Name, o.PriceDeltaUsd, o.MaxQuantity, o.IsAvailable, o.SortOrder))
                            .ToList()))
                    .ToList()))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"No menu item found with id '{id}'.");
    }

    private bool IsOpenNow(IEnumerable<RestaurantHours> hours)
    {
        var now = clock.LocalNow;
        return OpeningHours.IsOpenAt(hours, now.DayOfWeek, TimeOnly.FromDateTime(now.DateTime));
    }
}
