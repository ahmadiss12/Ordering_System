using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderingSystem.Api.IntegrationTests.Auth;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Features.Cart;
using OrderingSystem.Infrastructure.Persistence.Seed;

namespace OrderingSystem.Api.IntegrationTests.Cart;

/// <summary>
/// The basket, through the real pipeline.
/// <para>
/// The isolation that matters here is not the tenant guard — a basket belongs to a person, not a
/// restaurant — so these check that one customer cannot see or touch another's, which is a
/// different boundary from everything tested so far.
/// </para>
/// </summary>
public sealed class CartTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_basket_needs_somebody_to_belong_to()
    {
        var restaurantId = await RestaurantIdAsync();

        var response = await factory.CreateClient().GetAsync($"/api/restaurants/{restaurantId}/cart", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_untouched_basket_reads_as_empty_rather_than_missing()
    {
        var client = await SignInAsync("joe@example.test");
        var restaurantId = await RestaurantIdAsync();

        var cart = await client.GetFromJsonAsync<CartResponse>(
            $"/api/restaurants/{restaurantId}/cart", Ct);

        // A 404 would make every storefront handle "no basket yet" as an error case.
        cart!.Lines.ShouldBeEmpty();
        cart.ItemCount.ShouldBe(0);
        cart.SubtotalUsd.ShouldBe(0m);
        cart.RestaurantSlug.ShouldBe("frieslab");
    }

    [Fact]
    public async Task Adding_a_dish_prices_it_from_the_menu()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();
        await ClearAsync(client, restaurantId);

        // A drink: no option groups at all, so this test is about pricing and nothing else.
        var item = await ItemAsync("Coca-Cola");

        var cart = await AddAsync(client, restaurantId, new AddCartLineRequest(item.Id, 2, null, []));

        var line = cart.Lines.ShouldHaveSingleItem();
        line.Name.ShouldBe("Coca-Cola");
        line.Quantity.ShouldBe(2);

        // The price came from the menu, not from anything the client sent.
        line.UnitPriceUsd.ShouldBe(item.Price);
        line.LineTotalUsd.ShouldBe(item.Price * 2);
        cart.SubtotalUsd.ShouldBe(item.Price * 2);
    }

    [Fact]
    public async Task Options_add_their_own_price_to_the_line()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();
        await ClearAsync(client, restaurantId);

        // Loaded fries carry Extras, and nothing they require.
        var item = await ItemAsync("Cheese Lab Fries");
        var extra = await OptionAsync("Extra Cheese");

        var cart = await AddAsync(client, restaurantId,
            new AddCartLineRequest(item.Id, 1, null, [new ChosenOptionRequest(extra.Id, 1)]));

        var line = cart.Lines.ShouldHaveSingleItem();
        line.UnitPriceUsd.ShouldBe(item.Price + extra.Delta);
        line.Options.ShouldHaveSingleItem().Name.ShouldBe("Extra Cheese");
    }

    [Fact]
    public async Task The_same_dish_with_the_same_options_becomes_one_line()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();
        await ClearAsync(client, restaurantId);

        var item = await ItemAsync("Coca-Cola");

        await AddAsync(client, restaurantId, new AddCartLineRequest(item.Id, 1, null, []));
        var cart = await AddAsync(client, restaurantId, new AddCartLineRequest(item.Id, 2, null, []));

        // "One more of those", not a second row saying the same thing.
        cart.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(3);
    }

    [Fact]
    public async Task The_same_dish_with_different_options_stays_separate()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();
        await ClearAsync(client, restaurantId);

        var item = await ItemAsync("Cheese Lab Fries");
        var extra = await OptionAsync("Extra Cheese");

        await AddAsync(client, restaurantId, new AddCartLineRequest(item.Id, 1, null, []));
        var cart = await AddAsync(client, restaurantId,
            new AddCartLineRequest(item.Id, 1, null, [new ChosenOptionRequest(extra.Id, 1)]));

        // Merging them would lose one of the two customers' choices.
        cart.Lines.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_required_choice_cannot_be_skipped()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();

        // Every burger carries Size, which the seed declares as exactly one.
        var burger = await ItemAsync("Classic Smash");

        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/cart/lines",
            new AddCartLineRequest(burger.Id, 1, null, []), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldContain("Choose one from Size");
    }

    [Fact]
    public async Task A_burger_with_its_size_chosen_goes_in()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();
        await ClearAsync(client, restaurantId);

        var burger = await ItemAsync("Classic Smash");
        var large = await OptionAsync("Large");

        var cart = await AddAsync(client, restaurantId,
            new AddCartLineRequest(burger.Id, 1, null, [new ChosenOptionRequest(large.Id, 1)]));

        var line = cart.Lines.ShouldHaveSingleItem();
        line.UnitPriceUsd.ShouldBe(burger.Price + large.Delta);
        line.Options.ShouldHaveSingleItem().GroupName.ShouldBe("Size");

        await ClearAsync(client, restaurantId);
    }

    [Fact]
    public async Task Choosing_more_than_a_group_allows_is_refused_with_a_sentence()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();

        var item = await ItemAsync("Classic Smash");
        var sizes = await OptionsInGroupAsync("Size");

        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/cart/lines",
            new AddCartLineRequest(item.Id, 1, null,
                [.. sizes.Select(id => new ChosenOptionRequest(id, 1))]), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldContain("Size");
    }

    [Fact]
    public async Task An_option_from_another_restaurant_is_refused()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();

        var item = await ItemAsync("Classic Smash");
        var large = await OptionAsync("Large");
        var foreign = await ForeignOptionIdAsync();

        // Size is answered correctly, so the foreign option is the only thing wrong.
        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/cart/lines",
            new AddCartLineRequest(item.Id, 1, null,
                [new ChosenOptionRequest(large.Id, 1), new ChosenOptionRequest(foreign, 1)]), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_dish_from_another_restaurant_cannot_join_this_basket()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();
        var foreignItem = await ForeignItemIdAsync();

        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/cart/lines",
            new AddCartLineRequest(foreignItem, 1, null, []), Ct);

        // One basket, one kitchen. Otherwise it becomes an order nobody can fulfil.
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task One_customer_cannot_touch_anothers_basket()
    {
        var rita = await SignInAsync("rita@example.test");
        var joe = await SignInAsync("joe@example.test");
        var restaurantId = await RestaurantIdAsync();
        await ClearAsync(rita, restaurantId);

        var item = await ItemAsync("Bacon Lab");
        var large = await OptionAsync("Large");
        var cart = await AddAsync(rita, restaurantId,
            new AddCartLineRequest(item.Id, 1, null, [new ChosenOptionRequest(large.Id, 1)]));
        var lineId = cart.Lines[0].Id;

        var joesView = await joe.GetFromJsonAsync<CartResponse>(
            $"/api/restaurants/{restaurantId}/cart", Ct);
        joesView!.Lines.ShouldBeEmpty("baskets belong to a person, not to a restaurant");

        var removal = await joe.DeleteAsync(
            $"/api/restaurants/{restaurantId}/cart/lines/{lineId}", Ct);

        // Not found rather than forbidden: confirming it exists would tell Joe something about
        // Rita's basket.
        removal.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Changing_a_quantity_reprices_the_line()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();
        await ClearAsync(client, restaurantId);

        var item = await ItemAsync("Buffalo Wings");
        var cart = await AddAsync(client, restaurantId, new AddCartLineRequest(item.Id, 1, null, []));

        var response = await client.PutAsJsonAsync(
            $"/api/restaurants/{restaurantId}/cart/lines/{cart.Lines[0].Id}",
            new UpdateCartLineRequest(4, "extra spicy"), Ct);
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<CartResponse>(Ct);
        var line = updated!.Lines.ShouldHaveSingleItem();
        line.Quantity.ShouldBe(4);
        line.Note.ShouldBe("extra spicy");
        line.LineTotalUsd.ShouldBe(item.Price * 4);
    }

    [Fact]
    public async Task A_quantity_of_zero_is_not_how_a_line_is_removed()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();
        await ClearAsync(client, restaurantId);

        var item = await ItemAsync("Coca-Cola");
        var cart = await AddAsync(client, restaurantId, new AddCartLineRequest(item.Id, 1, null, []));

        var response = await client.PutAsJsonAsync(
            $"/api/restaurants/{restaurantId}/cart/lines/{cart.Lines[0].Id}",
            new UpdateCartLineRequest(0, null), Ct);

        // There is an endpoint for removing. Treating zero as one would make "set quantity"
        // silently destructive.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Emptying_the_basket_leaves_it_usable()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();

        var item = await ItemAsync("Coca-Cola");
        await AddAsync(client, restaurantId, new AddCartLineRequest(item.Id, 1, null, []));

        var cleared = await ClearAsync(client, restaurantId);
        cleared.Lines.ShouldBeEmpty();
        cleared.ItemCount.ShouldBe(0);

        var again = await AddAsync(client, restaurantId, new AddCartLineRequest(item.Id, 1, null, []));
        again.Lines.ShouldHaveSingleItem();

        await ClearAsync(client, restaurantId);
    }

    [Fact]
    public async Task A_basket_survives_signing_in_again()
    {
        var first = await SignInAsync("joe@example.test");
        var restaurantId = await RestaurantIdAsync();
        await ClearAsync(first, restaurantId);

        var item = await ItemAsync("Cheese Lab Fries");
        await AddAsync(first, restaurantId, new AddCartLineRequest(item.Id, 2, null, []));

        // The whole reason the cart is on the server: a different session is the same basket.
        var second = await SignInAsync("joe@example.test");
        var cart = await second.GetFromJsonAsync<CartResponse>(
            $"/api/restaurants/{restaurantId}/cart", Ct);

        cart!.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(2);
        await ClearAsync(second, restaurantId);
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

    private static async Task<CartResponse> AddAsync(HttpClient client, Guid restaurantId, AddCartLineRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/cart/lines", request, Ct);
        return await ReadAsync(response);
    }

    private static async Task<CartResponse> ClearAsync(HttpClient client, Guid restaurantId)
    {
        var response = await client.DeleteAsync($"/api/restaurants/{restaurantId}/cart", Ct);
        return await ReadAsync(response);
    }

    /// <summary>
    /// Reads the cart, and on failure says what the server actually replied.
    ///
    /// EnsureSuccessStatusCode reports only the status, which turns "why did this 500" into a
    /// hunt through server logs. The ProblemDetails body is right there and names the cause.
    /// </summary>
    private static async Task<CartResponse> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.IsSuccessStatusCode.ShouldBeTrue(
            $"the request failed with {(int)response.StatusCode}: {body}");

        return (await response.Content.ReadFromJsonAsync<CartResponse>(Ct))!;
    }

    private async Task<Guid> RestaurantIdAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Restaurants.Where(r => r.Slug == "frieslab").Select(r => r.Id).FirstAsync(Ct);
    }

    private async Task<(Guid Id, decimal Price)> ItemAsync(string name)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var row = await db.MenuItems
            .Where(i => i.Restaurant.Slug == "frieslab" && i.Name == name)
            .Select(i => new { i.Id, i.BasePriceUsd })
            .FirstAsync(Ct);
        return (row.Id, row.BasePriceUsd);
    }

    private async Task<(Guid Id, decimal Delta)> OptionAsync(string name)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var row = await db.Options
            .Where(o => o.OptionGroup.Restaurant.Slug == "frieslab" && o.Name == name)
            .Select(o => new { o.Id, o.PriceDeltaUsd })
            .FirstAsync(Ct);
        return (row.Id, row.PriceDeltaUsd);
    }

    private async Task<IReadOnlyList<Guid>> OptionsInGroupAsync(string groupName)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Options
            .Where(o => o.OptionGroup.Restaurant.Slug == "frieslab" && o.OptionGroup.Name == groupName)
            .Select(o => o.Id)
            .ToListAsync(Ct);
    }

    private async Task<Guid> ForeignItemIdAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.MenuItems
            .Where(i => i.Restaurant.Slug == "beirut-mezze-house")
            .Select(i => i.Id).FirstAsync(Ct);
    }

    private async Task<Guid> ForeignOptionIdAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Options
            .Where(o => o.OptionGroup.Restaurant.Slug == "beirut-mezze-house")
            .Select(o => o.Id).FirstAsync(Ct);
    }
}
