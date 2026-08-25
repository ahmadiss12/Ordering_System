using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderingSystem.Api.IntegrationTests.Auth;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Features.Catalogue;
using OrderingSystem.Domain.Menu;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Api.IntegrationTests.Catalogue;

/// <summary>
/// The storefront reads, through the real HTTP pipeline against a real SQL Server. These assert
/// two things the unit tests cannot: that the menu projection actually translates to SQL, and
/// that the global query filters do the hiding nobody wrote a WHERE clause for.
/// </summary>
public sealed class CatalogueTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client => factory.CreateClient();

    [Fact]
    public async Task The_menu_is_public_so_an_anonymous_caller_can_read_it()
    {
        var slug = await SeedRestaurantAsync();

        // No Authorization header is ever set on this client. A customer browses before deciding
        // whether to have an account at all.
        var response = await Client.GetAsync($"/api/restaurants/{slug}/menu", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var menu = (await response.Content.ReadFromJsonAsync<MenuResponse>(Ct))!;
        menu.Slug.ShouldBe(slug);
        menu.Categories.ShouldHaveSingleItem().Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_item_level_override_replaces_the_group_bounds()
    {
        var slug = await SeedRestaurantAsync();
        var menu = await GetMenuAsync(slug);

        var items = menu.Categories.Single().Items;

        // The shared group is (0, null) — optional, unlimited.
        var openItem = items.Single(i => i.Name == "Smash Burger");
        var openGroup = openItem.OptionGroups.ShouldHaveSingleItem();
        openGroup.MinSelect.ShouldBe(0);
        openGroup.MaxSelect.ShouldBeNull();

        // The same group attached to a second item, capped at 2 for that item alone. The client
        // is told 2 and never learns an override exists.
        var cappedItem = items.Single(i => i.Name == "Loaded Fries");
        var cappedGroup = cappedItem.OptionGroups.ShouldHaveSingleItem();
        cappedGroup.Id.ShouldBe(openGroup.Id, "it is genuinely the same shared group");
        cappedGroup.MinSelect.ShouldBe(0);
        cappedGroup.MaxSelect.ShouldBe(2);
    }

    [Fact]
    public async Task A_sold_out_item_stays_in_the_menu_marked_unavailable()
    {
        var slug = await SeedRestaurantAsync();

        await using (var db = factory.CreateDbContext(TestTenant.PlatformAdmin()))
        {
            var item = await db.MenuItems.SingleAsync(i => i.Name == "Loaded Fries" && i.Restaurant.Slug == slug, Ct);
            item.IsAvailable = false;
            await db.SaveChangesAsync(Ct);
        }

        var menu = await GetMenuAsync(slug);
        var fries = menu.Categories.Single().Items.Single(i => i.Name == "Loaded Fries");

        // Present, not absent. A disappearing item reads as a broken menu to a returning customer.
        fries.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task A_soft_deleted_item_disappears_without_anyone_writing_a_where_clause()
    {
        var slug = await SeedRestaurantAsync();

        await using (var db = factory.CreateDbContext(TestTenant.PlatformAdmin()))
        {
            var item = await db.MenuItems.SingleAsync(i => i.Name == "Loaded Fries" && i.Restaurant.Slug == slug, Ct);
            item.IsDeleted = true;
            await db.SaveChangesAsync(Ct);
        }

        var menu = await GetMenuAsync(slug);

        // CatalogueService has no IsDeleted condition anywhere. The global query filter applies
        // to the navigation inside the projection, which is the whole argument for having it.
        menu.Categories.Single().Items.ShouldHaveSingleItem().Name.ShouldBe("Smash Burger");
    }

    [Fact]
    public async Task An_inactive_restaurant_is_neither_listed_nor_readable()
    {
        var slug = await SeedRestaurantAsync(isActive: false);

        var detail = await Client.GetAsync($"/api/restaurants/{slug}", Ct);
        detail.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var listed = await Client.GetFromJsonAsync<List<RestaurantSummaryResponse>>(
            "/api/restaurants", Ct);
        listed!.ShouldNotContain(r => r.Slug == slug);
    }

    [Fact]
    public async Task An_unknown_slug_is_a_404()
    {
        var response = await Client.GetAsync(
            $"/api/restaurants/no-such-place-{Guid.NewGuid():N}/menu", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_slug_resolves_regardless_of_casing()
    {
        var slug = await SeedRestaurantAsync();

        var response = await Client.GetAsync(
            $"/api/restaurants/{slug.ToUpperInvariant()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<MenuResponse> GetMenuAsync(string slug) =>
        (await Client.GetFromJsonAsync<MenuResponse>(
            $"/api/restaurants/{slug}/menu", Ct))!;

    /// <summary>
    /// One restaurant, one category, two items, and one option group shared by both — capped on
    /// the second item only. That is the smallest fixture that exercises the override path.
    /// </summary>
    private async Task<string> SeedRestaurantAsync(bool isActive = true)
    {
        var slug = $"test-{Guid.NewGuid():N}";
        var restaurantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var burgerId = Guid.NewGuid();
        var friesId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        db.Restaurants.Add(new Restaurant
        {
            Id = restaurantId,
            Name = "Test Kitchen",
            Slug = slug,
            Phone = "+96170000001",
            IsActive = isActive,
            IsAcceptingOrders = true,
            CommissionPercent = 15m,
            MinOrderUsd = 8m,
            DefaultPrepMinutes = 20,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Categories.Add(new Category
        {
            Id = categoryId,
            RestaurantId = restaurantId,
            Name = "Mains",
            SortOrder = 0,
            IsActive = true,
        });

        db.MenuItems.Add(NewItem(burgerId, restaurantId, categoryId, "Smash Burger", 9.50m, 0));
        db.MenuItems.Add(NewItem(friesId, restaurantId, categoryId, "Loaded Fries", 6.00m, 1));

        db.OptionGroups.Add(new OptionGroup
        {
            Id = groupId,
            RestaurantId = restaurantId,
            Name = "Extras",
            MinSelect = 0,
            MaxSelect = null,
            SortOrder = 0,
        });

        db.Options.Add(NewOption(groupId, "Extra Cheese", 1.00m, 0));
        db.Options.Add(NewOption(groupId, "Jalapenos", 0.50m, 1));

        db.MenuItemOptionGroups.Add(new MenuItemOptionGroup
        {
            MenuItemId = burgerId,
            OptionGroupId = groupId,
            SortOrder = 0,
        });

        db.MenuItemOptionGroups.Add(new MenuItemOptionGroup
        {
            MenuItemId = friesId,
            OptionGroupId = groupId,
            SortOrder = 0,
            MaxSelectOverride = 2,
        });

        await db.SaveChangesAsync(Ct);
        return slug;
    }

    private static MenuItem NewItem(
        Guid id, Guid restaurantId, Guid categoryId, string name, decimal price, int sortOrder) => new()
    {
        Id = id,
        RestaurantId = restaurantId,
        CategoryId = categoryId,
        Name = name,
        BasePriceUsd = price,
        IsAvailable = true,
        SortOrder = sortOrder,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Option NewOption(Guid groupId, string name, decimal delta, int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        OptionGroupId = groupId,
        Name = name,
        PriceDeltaUsd = delta,
        MaxQuantity = 1,
        IsAvailable = true,
        SortOrder = sortOrder,
    };
}
