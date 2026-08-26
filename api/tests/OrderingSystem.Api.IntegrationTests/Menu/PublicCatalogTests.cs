using System.Net;
using System.Net.Http.Json;
using OrderingSystem.Api.IntegrationTests.Auth;
using OrderingSystem.Application.Common;
using OrderingSystem.Application.Features.Catalog;

namespace OrderingSystem.Api.IntegrationTests.Menu;

/// <summary>
/// What a visitor with no account can see. Read against the seeded marketplace, so these assert
/// the same data a person would be looking at.
/// </summary>
public sealed class PublicCatalogTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Anonymous => factory.CreateClient();

    [Fact]
    public async Task Browsing_requires_no_account()
    {
        var page = await Anonymous.GetFromJsonAsync<PagedResult<RestaurantSummary>>("/api/restaurants", Ct);

        page.ShouldNotBeNull();
        page.Items.Count.ShouldBe(3);
        page.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task An_oversized_page_request_is_clamped_rather_than_obeyed()
    {
        // An unbounded page size is a cheap request that is expensive to answer.
        var page = await Anonymous.GetFromJsonAsync<PagedResult<RestaurantSummary>>(
            "/api/restaurants?pageSize=100000", Ct);

        page!.PageSize.ShouldBeLessThanOrEqualTo(Paging.MaxPageSize);
    }

    [Fact]
    public async Task Filtering_by_zone_narrows_the_list_and_prices_delivery()
    {
        var all = await Anonymous.GetFromJsonAsync<PagedResult<RestaurantSummary>>("/api/restaurants", Ct);
        var detail = await Anonymous.GetFromJsonAsync<RestaurantDetail>("/api/restaurants/frieslab", Ct);
        var marMikhael = detail!.DeliversTo.First(z => z.ZoneName == "Mar Mikhael").ZoneId;

        var filtered = await Anonymous.GetFromJsonAsync<PagedResult<RestaurantSummary>>(
            $"/api/restaurants?zoneId={marMikhael}", Ct);

        filtered!.Items.ShouldNotBeEmpty();
        filtered.Items.Count.ShouldBeLessThan(all!.Items.Count, "not every restaurant delivers there");

        // The fee is only meaningful once a zone has been named, which is why it is nullable.
        filtered.Items.ShouldAllBe(r => r.DeliveryFeeUsd != null && r.EstimatedMinutes != null);
    }

    [Fact]
    public async Task A_menu_arrives_in_one_call_with_its_categories_in_order()
    {
        var menu = await Anonymous.GetFromJsonAsync<RestaurantMenu>("/api/restaurants/frieslab/menu", Ct);

        menu.ShouldNotBeNull();
        menu.Categories.ShouldNotBeEmpty();
        menu.Categories.Select(c => c.SortOrder).ShouldBeInOrder();
        menu.Categories.SelectMany(c => c.Items).ShouldNotBeEmpty();

        // Drinks have nothing to choose, so the client can add them straight from the list.
        var drinks = menu.Categories.First(c => c.Name == "Drinks");
        drinks.Items.ShouldAllBe(i => !i.HasOptions);
    }

    [Fact]
    public async Task An_item_detail_resolves_the_per_item_override_server_side()
    {
        var menu = await Anonymous.GetFromJsonAsync<RestaurantMenu>("/api/restaurants/frieslab/menu", Ct);
        var wings = menu!.Categories.First(c => c.Name == "Wings");

        var platterId = wings.Items.First(i => i.Name == "Wings Platter").Id;
        var buffaloId = wings.Items.First(i => i.Name == "Buffalo Wings").Id;

        var platter = await Anonymous.GetFromJsonAsync<MenuItemDetail>($"/api/menu-items/{platterId}", Ct);
        var buffalo = await Anonymous.GetFromJsonAsync<MenuItemDetail>($"/api/menu-items/{buffaloId}", Ct);

        // Same shared group, different caps. The client is handed the number that applies to the
        // item it asked about, never a group default plus an override to reconcile.
        platter!.OptionGroups.First(g => g.Name == "Sauces").MaxSelect.ShouldBe(5);
        buffalo!.OptionGroups.First(g => g.Name == "Sauces").MaxSelect.ShouldBe(3);
    }

    [Fact]
    public async Task An_item_detail_carries_every_option_shape_the_editor_must_render()
    {
        var menu = await Anonymous.GetFromJsonAsync<RestaurantMenu>("/api/restaurants/frieslab/menu", Ct);
        var burgerId = menu!.Categories.First(c => c.Name == "Smashed Burgers").Items[0].Id;

        var item = await Anonymous.GetFromJsonAsync<MenuItemDetail>($"/api/menu-items/{burgerId}", Ct);

        item!.OptionGroups.ShouldContain(g => g.MinSelect == 1 && g.MaxSelect == 1, "a required size");
        item.OptionGroups.ShouldContain(g => g.MinSelect == 0 && g.MaxSelect == null, "unlimited extras");
        item.OptionGroups.ShouldContain(g => g.MaxSelect == 3, "a capped group");
        item.OptionGroups.SelectMany(g => g.Options).ShouldContain(o => o.PriceDeltaUsd == 0m);
        item.OptionGroups.SelectMany(g => g.Options).ShouldContain(o => o.MaxQuantity > 1);
    }

    [Fact]
    public async Task An_unknown_slug_is_a_404_not_an_error()
    {
        var response = await Anonymous.GetAsync("/api/restaurants/no-such-restaurant/menu", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
