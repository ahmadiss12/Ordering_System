using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Menu;

namespace OrderingSystem.Application.Features.Menu;

/// <summary>
/// A restaurant editing its own menu.
/// <para>
/// Every method here follows the same shape, and the shape is the security model: load the row,
/// ask <see cref="ITenantGuard"/> whether this caller may act on it, only then change anything.
/// </para>
/// <para>
/// The load step deliberately does <em>not</em> filter by restaurant. Menu tables carry no tenant
/// query filter — the catalogue is public — so <c>db.MenuItems.FindAsync(id)</c> will happily
/// return another restaurant's burger. The guard is the only thing standing between that and an
/// edit, which is exactly why it is called on every single write rather than trusted to a filter.
/// </para>
/// </summary>
public sealed class MenuAdminService(
    IAppDbContext db, ITenantGuard guard, IValidationService validation, IClock clock, IImageStorage images)
{
    // ------------------------------------------------------------------ categories

    public async Task<IReadOnlyList<CategoryResponse>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var restaurantId = guard.RequireRestaurantId();

        return await db.Categories.AsNoTracking()
            .Where(c => c.RestaurantId == restaurantId)
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryResponse(c.Id, c.Name, c.SortOrder, c.IsActive))
            .ToListAsync(ct);
    }

    public async Task<CategoryResponse> CreateCategoryAsync(
        CreateCategoryRequest request, CancellationToken ct = default)
    {
        await validation.ValidateAsync(request, ct);
        var restaurantId = guard.RequireRestaurantId();

        var category = new Category
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Name = request.Name.Trim(),
            SortOrder = request.SortOrder,
            IsActive = true,
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);

        return new CategoryResponse(category.Id, category.Name, category.SortOrder, category.IsActive);
    }

    public async Task<CategoryResponse> UpdateCategoryAsync(
        Guid id, UpdateCategoryRequest request, CancellationToken ct = default)
    {
        await validation.ValidateAsync(request, ct);

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"No category found with id '{id}'.");

        guard.EnsureCanActFor(category.RestaurantId);

        category.Name = request.Name.Trim();
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);

        return new CategoryResponse(category.Id, category.Name, category.SortOrder, category.IsActive);
    }

    // ------------------------------------------------------------------ menu items

    public async Task<MenuItemResponse> CreateMenuItemAsync(
        CreateMenuItemRequest request, CancellationToken ct = default)
    {
        await validation.ValidateAsync(request, ct);
        var restaurantId = guard.RequireRestaurantId();

        // The category must belong to the same restaurant, or a staff member could file their
        // item under somebody else's menu section.
        var categoryOwner = await db.Categories.AsNoTracking()
            .Where(c => c.Id == request.CategoryId)
            .Select(c => (Guid?)c.RestaurantId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"No category found with id '{request.CategoryId}'.");

        guard.EnsureCanActFor(categoryOwner);

        var item = new MenuItem
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            CategoryId = request.CategoryId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            BasePriceUsd = request.BasePriceUsd,
            IsAvailable = true,
            SortOrder = request.SortOrder,
            CreatedAt = clock.UtcNow,
        };

        db.MenuItems.Add(item);
        await db.SaveChangesAsync(ct);

        return ToResponse(item);
    }

    public async Task<MenuItemResponse> UpdateMenuItemAsync(
        Guid id, UpdateMenuItemRequest request, CancellationToken ct = default)
    {
        await validation.ValidateAsync(request, ct);

        var item = await LoadOwnedItemAsync(id, ct);

        if (item.CategoryId != request.CategoryId)
        {
            var categoryOwner = await db.Categories.AsNoTracking()
                .Where(c => c.Id == request.CategoryId)
                .Select(c => (Guid?)c.RestaurantId)
                .FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException($"No category found with id '{request.CategoryId}'.");

            guard.EnsureCanActFor(categoryOwner);
            item.CategoryId = request.CategoryId;
        }

        item.Name = request.Name.Trim();
        item.Description = request.Description?.Trim();
        item.BasePriceUsd = request.BasePriceUsd;
        item.SortOrder = request.SortOrder;
        await db.SaveChangesAsync(ct);

        return ToResponse(item);
    }

    /// <summary>
    /// The button a kitchen presses when it runs out. Separate from update because it is pressed
    /// mid-service, on a phone, and must not require sending the whole item back.
    /// </summary>
    public async Task<MenuItemResponse> SetAvailabilityAsync(
        Guid id, SetAvailabilityRequest request, CancellationToken ct = default)
    {
        var item = await LoadOwnedItemAsync(id, ct);

        item.IsAvailable = request.IsAvailable;
        await db.SaveChangesAsync(ct);

        return ToResponse(item);
    }

    public async Task DeleteMenuItemAsync(Guid id, CancellationToken ct = default)
    {
        var item = await LoadOwnedItemAsync(id, ct);

        // Soft, always. Order lines point at this row, and a hard delete would erase what a past
        // customer actually bought.
        item.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ images

    /// <summary>
    /// Replaces an item's photo. The previous file is removed only after the new one is safely
    /// stored, so a failed upload leaves the old picture in place rather than none at all.
    /// </summary>
    public async Task<MenuItemResponse> SetImageAsync(
        Guid id, Stream content, CancellationToken ct = default)
    {
        var item = await LoadOwnedItemAsync(id, ct);
        var previous = item.ImageUrl;

        item.ImageUrl = await images.SaveAsync(content, ct);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(previous))
        {
            await images.DeleteAsync(previous, ct);
        }

        return ToResponse(item);
    }

    public async Task<MenuItemResponse> RemoveImageAsync(Guid id, CancellationToken ct = default)
    {
        var item = await LoadOwnedItemAsync(id, ct);
        var previous = item.ImageUrl;

        item.ImageUrl = null;
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(previous))
        {
            await images.DeleteAsync(previous, ct);
        }

        return ToResponse(item);
    }

    // ------------------------------------------------------------------ option groups

    public async Task<IReadOnlyList<OptionGroupResponse>> ListOptionGroupsAsync(CancellationToken ct = default)
    {
        var restaurantId = guard.RequireRestaurantId();

        return await db.OptionGroups.AsNoTracking()
            .Where(g => g.RestaurantId == restaurantId)
            .OrderBy(g => g.SortOrder)
            .Select(g => new OptionGroupResponse(
                g.Id, g.Name, g.MinSelect, g.MaxSelect, g.SortOrder,
                g.Options.OrderBy(o => o.SortOrder)
                    .Select(o => new OptionResponse(
                        o.Id, o.Name, o.PriceDeltaUsd, o.MaxQuantity, o.IsAvailable, o.SortOrder))
                    .ToList()))
            .ToListAsync(ct);
    }

    public async Task<OptionGroupResponse> CreateOptionGroupAsync(
        CreateOptionGroupRequest request, CancellationToken ct = default)
    {
        await validation.ValidateAsync(request, ct);
        var restaurantId = guard.RequireRestaurantId();

        var group = new OptionGroup
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Name = request.Name.Trim(),
            MinSelect = request.MinSelect,
            MaxSelect = request.MaxSelect,
            SortOrder = request.SortOrder,
        };

        db.OptionGroups.Add(group);
        await db.SaveChangesAsync(ct);

        return new OptionGroupResponse(group.Id, group.Name, group.MinSelect, group.MaxSelect, group.SortOrder, []);
    }

    public async Task<OptionGroupResponse> UpdateOptionGroupAsync(
        Guid id, UpdateOptionGroupRequest request, CancellationToken ct = default)
    {
        await validation.ValidateAsync(request, ct);

        var group = await db.OptionGroups.FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new NotFoundException($"No option group found with id '{id}'.");

        guard.EnsureCanActFor(group.RestaurantId);

        group.Name = request.Name.Trim();
        group.MinSelect = request.MinSelect;
        group.MaxSelect = request.MaxSelect;
        group.SortOrder = request.SortOrder;
        await db.SaveChangesAsync(ct);

        return new OptionGroupResponse(group.Id, group.Name, group.MinSelect, group.MaxSelect, group.SortOrder, []);
    }

    public async Task<OptionResponse> AddOptionAsync(
        Guid groupId, CreateOptionRequest request, CancellationToken ct = default)
    {
        await validation.ValidateAsync(request, ct);

        var group = await db.OptionGroups.AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new { g.Id, g.RestaurantId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"No option group found with id '{groupId}'.");

        guard.EnsureCanActFor(group.RestaurantId);

        var option = new Option
        {
            Id = Guid.NewGuid(),
            OptionGroupId = groupId,
            Name = request.Name.Trim(),
            PriceDeltaUsd = request.PriceDeltaUsd,
            MaxQuantity = request.MaxQuantity,
            IsAvailable = true,
            SortOrder = request.SortOrder,
        };

        db.Options.Add(option);
        await db.SaveChangesAsync(ct);

        return new OptionResponse(
            option.Id, option.Name, option.PriceDeltaUsd, option.MaxQuantity, option.IsAvailable, option.SortOrder);
    }

    public async Task<OptionResponse> UpdateOptionAsync(
        Guid id, UpdateOptionRequest request, CancellationToken ct = default)
    {
        await validation.ValidateAsync(request, ct);

        var option = await db.Options.Include(o => o.OptionGroup).FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException($"No option found with id '{id}'.");

        guard.EnsureCanActFor(option.OptionGroup.RestaurantId);

        option.Name = request.Name.Trim();
        option.PriceDeltaUsd = request.PriceDeltaUsd;
        option.MaxQuantity = request.MaxQuantity;
        option.IsAvailable = request.IsAvailable;
        option.SortOrder = request.SortOrder;
        await db.SaveChangesAsync(ct);

        return new OptionResponse(
            option.Id, option.Name, option.PriceDeltaUsd, option.MaxQuantity, option.IsAvailable, option.SortOrder);
    }

    // ------------------------------------------------------------------ attaching groups to items

    public async Task AttachOptionGroupAsync(
        Guid menuItemId, AttachOptionGroupRequest request, CancellationToken ct = default)
    {
        await validation.ValidateAsync(request, ct);

        var item = await LoadOwnedItemAsync(menuItemId, ct);

        var groupOwner = await db.OptionGroups.AsNoTracking()
            .Where(g => g.Id == request.OptionGroupId)
            .Select(g => (Guid?)g.RestaurantId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"No option group found with id '{request.OptionGroupId}'.");

        // Both sides checked. Attaching another restaurant's group to your own item would leak
        // their pricing onto your menu.
        guard.EnsureCanActFor(groupOwner);

        var existing = await db.MenuItemOptionGroups
            .FirstOrDefaultAsync(m => m.MenuItemId == menuItemId && m.OptionGroupId == request.OptionGroupId, ct);

        if (existing is null)
        {
            db.MenuItemOptionGroups.Add(new MenuItemOptionGroup
            {
                MenuItemId = item.Id,
                OptionGroupId = request.OptionGroupId,
                SortOrder = request.SortOrder,
                MinSelectOverride = request.MinSelectOverride,
                MaxSelectOverride = request.MaxSelectOverride,
            });
        }
        else
        {
            existing.SortOrder = request.SortOrder;
            existing.MinSelectOverride = request.MinSelectOverride;
            existing.MaxSelectOverride = request.MaxSelectOverride;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DetachOptionGroupAsync(Guid menuItemId, Guid optionGroupId, CancellationToken ct = default)
    {
        await LoadOwnedItemAsync(menuItemId, ct);

        var link = await db.MenuItemOptionGroups
            .FirstOrDefaultAsync(m => m.MenuItemId == menuItemId && m.OptionGroupId == optionGroupId, ct)
            ?? throw new NotFoundException("That option group is not attached to this item.");

        // A hard delete is right here: the link carries no history, and order lines snapshot the
        // group name rather than pointing at this row.
        db.MenuItemOptionGroups.Remove(link);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<MenuItem> LoadOwnedItemAsync(Guid id, CancellationToken ct)
    {
        var item = await db.MenuItems.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException($"No menu item found with id '{id}'.");

        guard.EnsureCanActFor(item.RestaurantId);
        return item;
    }

    private static MenuItemResponse ToResponse(MenuItem item) =>
        new(item.Id, item.CategoryId, item.Name, item.Description,
            item.BasePriceUsd, item.ImageUrl, item.IsAvailable, item.SortOrder);
}
