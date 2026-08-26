using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderingSystem.Api.IntegrationTests.Auth;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Features.Catalog;
using OrderingSystem.Application.Features.Menu;
using OrderingSystem.Infrastructure.Persistence.Seed;

namespace OrderingSystem.Api.IntegrationTests.Menu;

/// <summary>
/// Menu editing, and the property the spec calls the most important in the system: a staff member
/// hitting another restaurant's resource by id gets a 403.
/// <para>
/// Step 8 proved that at the data layer. These prove it through the real pipeline — policy, token,
/// controller, guard — which is the path an attacker would actually take.
/// </para>
/// </summary>
public sealed class MenuAdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anonymous_callers_cannot_reach_the_editor()
    {
        var response = await factory.CreateClient().GetAsync("/api/restaurant/categories", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_customer_is_refused_even_though_they_are_signed_in()
    {
        // A customer's token carries no restaurant_id, and the policy requires one: a role
        // without a restaurant cannot be scoped to anything.
        var client = await SignInAsync("rita@example.test");

        var response = await client.GetAsync("/api/restaurant/categories", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Staff_see_only_their_own_restaurants_categories()
    {
        var client = await SignInAsync("staff@frieslab.test");

        var categories = await client.GetFromJsonAsync<IReadOnlyList<CategoryResponse>>(
            "/api/restaurant/categories", Ct);

        categories.ShouldNotBeEmpty();
        categories!.Select(c => c.Name).ShouldContain("Smashed Burgers");
        categories.Select(c => c.Name).ShouldNotContain("Cold Mezze", "that belongs to another restaurant");
    }

    [Fact]
    public async Task Staff_can_add_a_category_to_their_own_menu()
    {
        var client = await SignInAsync("staff@frieslab.test");

        var response = await client.PostAsJsonAsync("/api/restaurant/categories",
            new CreateCategoryRequest($"Specials {Guid.NewGuid():N}"[..20], 99), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Editing_another_restaurants_category_is_refused()
    {
        var client = await SignInAsync("staff@frieslab.test");
        var foreignCategoryId = await ForeignCategoryIdAsync();

        // Knowing the id is not access. This is the direct-API-call case from spec section 4.
        var response = await client.PutAsJsonAsync($"/api/restaurant/categories/{foreignCategoryId}",
            new UpdateCategoryRequest("Renamed by an outsider", 0, true), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Editing_another_restaurants_item_is_refused()
    {
        var client = await SignInAsync("staff@frieslab.test");
        var foreignItemId = await ForeignItemIdAsync();

        var response = await client.PatchAsJsonAsync(
            $"/api/restaurant/menu-items/{foreignItemId}/availability",
            new SetAvailabilityRequest(false), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deleting_another_restaurants_item_is_refused()
    {
        var client = await SignInAsync("staff@frieslab.test");
        var foreignItemId = await ForeignItemIdAsync();

        var response = await client.DeleteAsync($"/api/restaurant/menu-items/{foreignItemId}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Attaching_another_restaurants_option_group_is_refused()
    {
        var client = await SignInAsync("staff@frieslab.test");
        var ownItemId = await OwnItemIdAsync();
        var foreignGroupId = await ForeignOptionGroupIdAsync();

        // Both ends are checked. Attaching someone else's group to your own item would put their
        // pricing on your menu, and the item being yours is not enough to allow it.
        var response = await client.PutAsJsonAsync($"/api/restaurant/menu-items/{ownItemId}/option-groups",
            new AttachOptionGroupRequest(foreignGroupId, 0, null, null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Marking_an_item_unavailable_shows_up_on_the_public_menu()
    {
        var client = await SignInAsync("staff@frieslab.test");
        var itemId = await OwnItemIdAsync("Fresh Lemonade");

        var patched = await client.PatchAsJsonAsync($"/api/restaurant/menu-items/{itemId}/availability",
            new SetAvailabilityRequest(false), Ct);
        patched.StatusCode.ShouldBe(HttpStatusCode.OK);

        var menu = await factory.CreateClient()
            .GetFromJsonAsync<RestaurantMenu>("/api/restaurants/frieslab/menu", Ct);

        var item = menu!.Categories.SelectMany(c => c.Items).First(i => i.Id == itemId);

        // Still listed, greyed out rather than vanished: an item that disappears reads as a
        // broken menu to a returning customer.
        item.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task A_deleted_item_leaves_the_public_menu_entirely()
    {
        var client = await SignInAsync("staff@frieslab.test");
        var itemId = await OwnItemIdAsync("Still Water");

        var deleted = await client.DeleteAsync($"/api/restaurant/menu-items/{itemId}", Ct);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var menu = await factory.CreateClient()
            .GetFromJsonAsync<RestaurantMenu>("/api/restaurants/frieslab/menu", Ct);

        menu!.Categories.SelectMany(c => c.Items).ShouldNotContain(i => i.Id == itemId);

        // Gone from the menu, still in the table - the global soft-delete filter hides it while
        // order lines keep resolving.
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        (await db.MenuItems.IgnoreQueryFilters().AnyAsync(i => i.Id == itemId, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_option_group_whose_minimum_exceeds_its_maximum_is_rejected()
    {
        var client = await SignInAsync("staff@frieslab.test");

        var response = await client.PostAsJsonAsync("/api/restaurant/option-groups",
            new CreateOptionGroupRequest("Impossible", MinSelect: 5, MaxSelect: 2, SortOrder: 0), Ct);

        // Caught by the validator so the caller gets a sentence, not a constraint violation - but
        // CK_OptionGroups_SelectRange would refuse it regardless of what the API did.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<HttpClient> SignInAsync(string email)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new OrderingSystem.Application.Features.Auth.LoginRequest(email, DatabaseSeeder.SeedPassword), Ct);
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content
            .ReadFromJsonAsync<OrderingSystem.Application.Features.Auth.AuthTokensResponse>(Ct);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private async Task<Guid> OwnItemIdAsync(string? name = null)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.MenuItems
            .Where(i => i.Restaurant.Slug == "frieslab" && (name == null || i.Name == name))
            .Select(i => i.Id)
            .FirstAsync(Ct);
    }

    private async Task<Guid> ForeignItemIdAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.MenuItems
            .Where(i => i.Restaurant.Slug == "beirut-mezze-house")
            .Select(i => i.Id).FirstAsync(Ct);
    }

    private async Task<Guid> ForeignCategoryIdAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Categories
            .Where(c => c.Restaurant.Slug == "beirut-mezze-house")
            .Select(c => c.Id).FirstAsync(Ct);
    }

    private async Task<Guid> ForeignOptionGroupIdAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.OptionGroups
            .Where(g => g.Restaurant.Slug == "beirut-mezze-house")
            .Select(g => g.Id).FirstAsync(Ct);
    }
}
