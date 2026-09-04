using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderingSystem.Api.IntegrationTests.Auth;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Features.Cart;
using OrderingSystem.Application.Features.Orders;
using OrderingSystem.Application.Features.Restaurants;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Infrastructure.Persistence.Seed;

/// <summary>
/// A restaurant changing its own settings.
///
/// <para>
/// Two things are being tested and only one of them is CRUD. The other is that these fields are
/// live: the minimum order and the prep time decide what the next customer is refused and
/// promised, so who may edit them matters as much as whether the edit saves — and an edit must
/// never reach an order that has already been placed.
/// </para>
/// </summary>
namespace OrderingSystem.Api.IntegrationTests.Restaurants;

public sealed class RestaurantSettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------ reading

    [Fact]
    public async Task Staff_can_read_their_own_restaurants_settings()
    {
        var staff = await SignInAsync("staff@frieslab.test");

        var settings = await staff.GetFromJsonAsync<RestaurantSettingsResponse>(
            "/api/restaurant/settings", Ct);

        settings!.Name.ShouldBe("FriesLab");
        settings.Slug.ShouldBe("frieslab");
    }

    [Fact]
    public async Task A_restaurant_is_told_what_it_is_being_charged()
    {
        var owner = await SignInAsync("owner@frieslab.test");

        var settings = await owner.GetFromJsonAsync<RestaurantSettingsResponse>(
            "/api/restaurant/settings", Ct);

        // Readable but not writable. A restaurant is entitled to see its commission; only the
        // platform may change it, because it is their revenue and the restaurant's cost at once.
        settings!.CommissionPercent.ShouldBeGreaterThan(0);
        settings.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task A_customer_cannot_reach_a_restaurants_settings()
    {
        var customer = await SignInAsync("rita@example.test");

        var response = await customer.GetAsync("/api/restaurant/settings", Ct);

        // No restaurant_id claim, so the policy refuses before anything is loaded.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ writing

    [Fact]
    public async Task An_owner_can_change_the_profile()
    {
        var owner = await SignInAsync("owner@frieslab.test");
        var before = await SettingsAsync(owner);

        try
        {
            var updated = await UpdateAsync(owner, before with { Description = "Fried, twice." });

            updated.Description.ShouldBe("Fried, twice.");
            // Everything not being changed comes back untouched, because the endpoint replaces
            // the row rather than patching it and a caller sends the whole thing.
            updated.Name.ShouldBe(before.Name);
            updated.Slug.ShouldBe(before.Slug);
        }
        finally
        {
            await UpdateAsync(owner, before);
        }
    }

    [Fact]
    public async Task Staff_cannot_change_the_profile()
    {
        var staff = await SignInAsync("staff@frieslab.test");
        var settings = await SettingsAsync(await SignInAsync("owner@frieslab.test"));

        var response = await staff.PutAsJsonAsync("/api/restaurant/settings",
            ToRequest(settings with { Name = "Somebody Else's Idea" }), Ct);

        // The name and the phone are what a customer sees, and the minimum is what they are
        // refused by. A cook changing those on a busy night is not a feature.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Staff_can_pause_orders_without_an_owner()
    {
        var staff = await SignInAsync("staff@frieslab.test");

        try
        {
            // The one thing on this screen a cook needs at eight on a Friday, so it does not sit
            // behind a form only an owner may submit.
            var paused = await SetAcceptingAsync(staff, false);
            paused.IsAcceptingOrders.ShouldBeFalse();

            var resumed = await SetAcceptingAsync(staff, true);
            resumed.IsAcceptingOrders.ShouldBeTrue();
        }
        finally
        {
            await SetAcceptingAsync(staff, true);
        }
    }

    [Fact]
    public async Task A_paused_restaurant_refuses_a_new_order_and_says_why()
    {
        var staff = await SignInAsync("staff@frieslab.test");
        var customer = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();

        await StockBasketAsync(customer, restaurantId);
        await SetAcceptingAsync(staff, false);

        try
        {
            var refused = await CheckoutAsync(customer, restaurantId);

            // The switch is not decoration: it is read by the checkout on every order.
            refused.Status.ShouldBe(HttpStatusCode.Conflict);
            refused.Body.ShouldContain("paused");
        }
        finally
        {
            await SetAcceptingAsync(staff, true);
            await ClearBasketAsync(customer, restaurantId);
        }
    }

    // ------------------------------------------------------------------ the live fields

    [Fact]
    public async Task Raising_the_minimum_refuses_the_next_order_and_leaves_the_last_one_alone()
    {
        var owner = await SignInAsync("owner@frieslab.test");
        var customer = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();
        var before = await SettingsAsync(owner);

        // An order placed under the old minimum.
        var placed = await PlaceOrderAsync(customer, restaurantId);

        try
        {
            await UpdateAsync(owner, before with { MinOrderUsd = 500m });

            // The next customer is refused, so the field is genuinely live.
            await StockBasketAsync(customer, restaurantId);
            var refused = await CheckoutAsync(customer, restaurantId);
            refused.Status.ShouldBe(HttpStatusCode.Conflict);
            refused.Body.ShouldContain("minimum order");

            // And the order already placed is untouched. This is the property Phase 3 built the
            // snapshot columns for, and the one an edit here could quietly break.
            var after = await customer.GetFromJsonAsync<OrderDetailResponse>(
                $"/api/orders/{placed.Id}", Ct);

            after!.TotalUsd.ShouldBe(placed.TotalUsd);
            after.Status.ShouldBe(OrderStatus.Placed);
        }
        finally
        {
            await UpdateAsync(owner, before);
            await ClearBasketAsync(customer, restaurantId);
        }
    }

    [Fact]
    public async Task Changing_the_prep_time_does_not_move_a_promise_already_made()
    {
        var owner = await SignInAsync("owner@frieslab.test");
        var customer = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();
        var before = await SettingsAsync(owner);

        var placed = await PlaceOrderAsync(customer, restaurantId);

        try
        {
            await UpdateAsync(owner, before with { DefaultPrepMinutes = before.DefaultPrepMinutes + 30 });

            var after = await customer.GetFromJsonAsync<OrderDetailResponse>(
                $"/api/orders/{placed.Id}", Ct);

            // A customer judges a restaurant by the promise it made them, not by the one it would
            // make today.
            after!.PromisedMinutesMin.ShouldBe(placed.PromisedMinutesMin);
            after.PromisedMinutesMax.ShouldBe(placed.PromisedMinutesMax);
        }
        finally
        {
            await UpdateAsync(owner, before);
        }
    }

    // ------------------------------------------------------------------ isolation and validation

    [Fact]
    public async Task One_restaurant_cannot_edit_another()
    {
        var stranger = await SignInAsync("owner@mezze.test");
        var friesLab = await SettingsAsync(await SignInAsync("owner@frieslab.test"));

        // There is no id in the route to change, so the only way to try is to send FriesLab's own
        // values and see whose row moves.
        var updated = await UpdateAsync(stranger, friesLab with { Description = "Not mine to set." });

        // Their own restaurant, edited. FriesLab untouched.
        updated.Slug.ShouldBe("beirut-mezze-house");

        var friesLabNow = await SettingsAsync(await SignInAsync("owner@frieslab.test"));
        friesLabNow.Description.ShouldBe(friesLab.Description);

        await UpdateAsync(stranger, ResetOf(updated));
    }

    [Theory]
    [InlineData("", 20, 8, "a restaurant with no name")]
    [InlineData("FriesLab", 0, 8, "a prep time of nothing")]
    [InlineData("FriesLab", 600, 8, "ten hours of prep")]
    [InlineData("FriesLab", 20, -1, "a negative minimum")]
    [InlineData("FriesLab", 20, 100000, "a mistyped minimum")]
    public async Task Nonsense_is_refused_with_a_reason(
        string name, int prepMinutes, decimal minOrder, string why)
    {
        var owner = await SignInAsync("owner@frieslab.test");

        var response = await owner.PutAsJsonAsync("/api/restaurant/settings",
            new UpdateRestaurantSettingsRequest(name, null, "+9611234567", prepMinutes, minOrder), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, why);
    }

    [Fact]
    public async Task A_description_of_only_spaces_is_stored_as_nothing()
    {
        var owner = await SignInAsync("owner@frieslab.test");
        var before = await SettingsAsync(owner);

        try
        {
            var updated = await UpdateAsync(owner, before with { Description = "   " });

            // Empty and whitespace both mean "not set", and storing one of them as a string makes
            // "has a description" two different questions.
            updated.Description.ShouldBeNull();
        }
        finally
        {
            await UpdateAsync(owner, before);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static UpdateRestaurantSettingsRequest ToRequest(RestaurantSettingsResponse settings) =>
        new(settings.Name, settings.Description, settings.Phone,
            settings.DefaultPrepMinutes, settings.MinOrderUsd);

    /// <summary>The mezze house's seeded values, for putting it back after the isolation test.</summary>
    private static RestaurantSettingsResponse ResetOf(RestaurantSettingsResponse current) =>
        current with { Description = "Small plates, charcoal grill, and a very good hummus." };

    private static async Task<RestaurantSettingsResponse> SettingsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<RestaurantSettingsResponse>("/api/restaurant/settings", Ct))!;

    private static async Task<RestaurantSettingsResponse> UpdateAsync(
        HttpClient client, RestaurantSettingsResponse settings)
    {
        var response = await client.PutAsJsonAsync("/api/restaurant/settings", ToRequest(settings), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"update failed with {(int)response.StatusCode}: {body}");

        return (await response.Content.ReadFromJsonAsync<RestaurantSettingsResponse>(Ct))!;
    }

    private static async Task<RestaurantSettingsResponse> SetAcceptingAsync(HttpClient client, bool accepting)
    {
        var response = await client.PatchAsJsonAsync("/api/restaurant/settings/accepting-orders",
            new SetAcceptingOrdersRequest(accepting), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"pausing failed with {(int)response.StatusCode}: {body}");

        return (await response.Content.ReadFromJsonAsync<RestaurantSettingsResponse>(Ct))!;
    }

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

    private async Task StockBasketAsync(HttpClient client, Guid restaurantId)
    {
        await ClearBasketAsync(client, restaurantId);

        var added = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/cart/lines",
            new AddCartLineRequest(await ItemIdAsync("Cheese Lab Fries"), 2, null, []), Ct);

        added.IsSuccessStatusCode.ShouldBeTrue(await added.Content.ReadAsStringAsync(Ct));
    }

    private static async Task ClearBasketAsync(HttpClient client, Guid restaurantId) =>
        (await client.DeleteAsync($"/api/restaurants/{restaurantId}/cart", Ct)).EnsureSuccessStatusCode();

    private static async Task<(HttpStatusCode Status, string Body)> CheckoutAsync(
        HttpClient client, Guid restaurantId)
    {
        var quote = await client.GetFromJsonAsync<QuoteResponse>(
            $"/api/restaurants/{restaurantId}/cart/quote?fulfillment=Pickup", Ct);

        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/orders",
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null,
                quote!.TotalUsd, Guid.NewGuid()), Ct);

        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<PlacedOrderResponse> PlaceOrderAsync(HttpClient client, Guid restaurantId)
    {
        await StockBasketAsync(client, restaurantId);

        var quote = await client.GetFromJsonAsync<QuoteResponse>(
            $"/api/restaurants/{restaurantId}/cart/quote?fulfillment=Pickup", Ct);

        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/orders",
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null,
                quote!.TotalUsd, Guid.NewGuid()), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"checkout failed: {body}");

        return (await response.Content.ReadFromJsonAsync<PlacedOrderResponse>(Ct))!;
    }

    private async Task<Guid> RestaurantIdAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Restaurants.Where(r => r.Slug == "frieslab").Select(r => r.Id).FirstAsync(Ct);
    }

    private async Task<Guid> ItemIdAsync(string name)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.MenuItems
            .Where(i => i.Restaurant.Slug == "frieslab" && i.Name == name)
            .Select(i => i.Id).FirstAsync(Ct);
    }
}
