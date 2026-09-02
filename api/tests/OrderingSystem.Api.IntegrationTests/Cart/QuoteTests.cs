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
/// What a basket costs, through the real pipeline and against the seeded restaurant.
/// <para>
/// The arithmetic itself is covered without a database in OrderPricingTests. These check the
/// other half — that the right numbers are fetched and handed to it, and that a customer cannot
/// influence any of them.
/// </para>
/// </summary>
public sealed class QuoteTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_pickup_quote_charges_no_delivery_and_promises_only_prep_time()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await ClearAsync(client, restaurant.Id);

        var coke = await ItemAsync("Coca-Cola");
        await AddAsync(client, restaurant.Id, coke.Id, quantity: 3);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");

        quote.SubtotalUsd.ShouldBe(coke.Price * 3);
        quote.DeliveryFeeUsd.ShouldBe(0m);
        quote.TotalUsd.ShouldBe(quote.SubtotalUsd);
        quote.DeliveryZoneName.ShouldBeNull();

        // Nothing travels anywhere, so the promise is the kitchen's time and nothing else.
        quote.PromisedMinutesMin.ShouldBe(restaurant.PrepMinutes);
        quote.PromisedMinutesMax.ShouldBeGreaterThan(quote.PromisedMinutesMin);

        await ClearAsync(client, restaurant.Id);
    }

    [Fact]
    public async Task A_delivery_quote_adds_the_zones_fee_and_its_travel_time()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await ClearAsync(client, restaurant.Id);

        var coke = await ItemAsync("Coca-Cola");
        await AddAsync(client, restaurant.Id, coke.Id, quantity: 4);

        var address = await ServedAddressAsync("rita@example.test", restaurant.Id);
        var quote = await QuoteAsync(client, restaurant.Id, "Delivery", address.Id);

        quote.DeliveryFeeUsd.ShouldBe(address.FeeUsd);
        quote.TotalUsd.ShouldBe(quote.SubtotalUsd + address.FeeUsd);
        quote.DeliveryZoneName.ShouldBe(address.ZoneName);

        // Prep plus travel, so the customer is told when food arrives rather than when it is cooked.
        quote.PromisedMinutesMin.ShouldBe(restaurant.PrepMinutes + address.TravelMinutes);

        await ClearAsync(client, restaurant.Id);
    }

    [Fact]
    public async Task The_lines_add_up_to_the_subtotal_to_the_cent()
    {
        var client = await SignInAsync("joe@example.test");
        var restaurant = await RestaurantAsync();
        await ClearAsync(client, restaurant.Id);

        // A mixture, so the sum is not trivially one multiplication.
        foreach (var name in new[] { "Coca-Cola", "Cheese Lab Fries", "Buffalo Wings" })
        {
            var item = await ItemAsync(name);
            await AddAsync(client, restaurant.Id, item.Id, quantity: 2);
        }

        var cart = await client.GetFromJsonAsync<CartResponse>(
            $"/api/restaurants/{restaurant.Id}/cart", Ct);
        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");

        // The number a customer checks by hand, and the number the cart badge shows.
        quote.SubtotalUsd.ShouldBe(cart!.Lines.Sum(l => l.LineTotalUsd));
        quote.SubtotalUsd.ShouldBe(cart.SubtotalUsd);
        quote.ItemCount.ShouldBe(6);

        await ClearAsync(client, restaurant.Id);
    }

    [Fact]
    public async Task A_basket_below_the_minimum_says_how_much_more_is_needed()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await ClearAsync(client, restaurant.Id);

        var coke = await ItemAsync("Coca-Cola");
        await AddAsync(client, restaurant.Id, coke.Id, quantity: 1);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");

        // FriesLab's minimum is $8 and a drink is well under it.
        quote.MinOrderUsd.ShouldBe(restaurant.MinOrderUsd);
        quote.MeetsMinimum.ShouldBeFalse();
        quote.ShortfallUsd.ShouldBe(restaurant.MinOrderUsd - quote.SubtotalUsd);

        // A quote, not a refusal: the screen says "add $X more", it does not throw the basket away.
        quote.TotalUsd.ShouldBeGreaterThan(0m);

        await ClearAsync(client, restaurant.Id);
    }

    [Fact]
    public async Task A_delivery_fee_never_carries_a_basket_over_the_minimum()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await ClearAsync(client, restaurant.Id);

        var coke = await ItemAsync("Coca-Cola");
        await AddAsync(client, restaurant.Id, coke.Id, quantity: 1);

        var address = await ServedAddressAsync("rita@example.test", restaurant.Id);
        var quote = await QuoteAsync(client, restaurant.Id, "Delivery", address.Id);

        // Otherwise the minimum would mean nothing: it exists to make the kitchen's trip
        // worthwhile, and the fee is not food.
        quote.MeetsMinimum.ShouldBe(quote.SubtotalUsd >= restaurant.MinOrderUsd);
        quote.ShortfallUsd.ShouldBe(restaurant.MinOrderUsd - quote.SubtotalUsd);

        await ClearAsync(client, restaurant.Id);
    }

    [Fact]
    public async Task Delivering_somewhere_the_restaurant_does_not_serve_is_refused_by_name()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        var unserved = await UnservedZoneAddressAsync("rita@example.test", restaurant.Id);

        if (unserved is null)
        {
            // Every seeded zone happens to be served. Nothing to prove here, and inventing an
            // address would be testing the seeder rather than the rule.
            return;
        }

        var response = await client.GetAsync(
            $"/api/restaurants/{restaurant.Id}/cart/quote?fulfillment=Delivery&addressId={unserved.Value.Id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain(unserved.Value.ZoneName);
    }

    [Fact]
    public async Task A_delivery_quote_without_an_address_asks_for_one()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();

        var response = await client.GetAsync(
            $"/api/restaurants/{restaurant.Id}/cart/quote?fulfillment=Delivery", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("addressId");
    }

    [Fact]
    public async Task Somebody_elses_address_cannot_be_quoted_against()
    {
        var client = await SignInAsync("joe@example.test");
        var restaurant = await RestaurantAsync();
        var ritas = await ServedAddressAsync("rita@example.test", restaurant.Id);

        var response = await client.GetAsync(
            $"/api/restaurants/{restaurant.Id}/cart/quote?fulfillment=Delivery&addressId={ritas.Id}", Ct);

        // Not found rather than forbidden: confirming it exists would tell Joe something about
        // where Rita lives.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_empty_basket_quotes_at_zero_rather_than_failing()
    {
        var client = await SignInAsync("joe@example.test");
        var restaurant = await RestaurantAsync();
        await ClearAsync(client, restaurant.Id);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");

        quote.SubtotalUsd.ShouldBe(0m);
        quote.TotalUsd.ShouldBe(0m);
        quote.ItemCount.ShouldBe(0);
        quote.MeetsMinimum.ShouldBeFalse();
    }

    [Fact]
    public async Task Tax_is_reported_as_zero_rather_than_omitted()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");

        quote.TaxUsd.ShouldBe(0m);
    }

    [Fact]
    public async Task A_total_in_pounds_uses_the_rate_that_is_in_force()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await ClearAsync(client, restaurant.Id);

        var coke = await ItemAsync("Coca-Cola");
        await AddAsync(client, restaurant.Id, coke.Id, quantity: 2);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");
        var rate = await CurrentRateAsync();

        quote.TotalLbp.ShouldNotBeNull();
        quote.TotalLbp!.Value.ShouldBe(decimal.Round(quote.TotalUsd * rate, 0, MidpointRounding.AwayFromZero));

        // Whole pounds. There is no smaller unit in circulation.
        quote.TotalLbp.Value.ShouldBe(decimal.Truncate(quote.TotalLbp.Value));

        await ClearAsync(client, restaurant.Id);
    }

    [Fact]
    public async Task A_quote_needs_somebody_to_belong_to()
    {
        var restaurant = await RestaurantAsync();

        var response = await factory.CreateClient()
            .GetAsync($"/api/restaurants/{restaurant.Id}/cart/quote?fulfillment=Pickup", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
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

    private static async Task<QuoteResponse> QuoteAsync(
        HttpClient client, Guid restaurantId, string fulfillment, Guid? addressId = null)
    {
        var url = $"/api/restaurants/{restaurantId}/cart/quote?fulfillment={fulfillment}"
            + (addressId is null ? string.Empty : $"&addressId={addressId}");

        var response = await client.GetAsync(url, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.IsSuccessStatusCode.ShouldBeTrue($"the quote failed with {(int)response.StatusCode}: {body}");
        return (await response.Content.ReadFromJsonAsync<QuoteResponse>(Ct))!;
    }

    private static async Task AddAsync(HttpClient client, Guid restaurantId, Guid itemId, int quantity)
    {
        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/cart/lines",
            new AddCartLineRequest(itemId, quantity, null, []), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"adding failed with {(int)response.StatusCode}: {body}");
    }

    private static async Task ClearAsync(HttpClient client, Guid restaurantId)
    {
        var response = await client.DeleteAsync($"/api/restaurants/{restaurantId}/cart", Ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<(Guid Id, int PrepMinutes, decimal MinOrderUsd)> RestaurantAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var row = await db.Restaurants
            .Where(r => r.Slug == "frieslab")
            .Select(r => new { r.Id, r.DefaultPrepMinutes, r.MinOrderUsd })
            .FirstAsync(Ct);
        return (row.Id, row.DefaultPrepMinutes, row.MinOrderUsd);
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

    /// <summary>An address of this customer's that the restaurant actually delivers to.</summary>
    private async Task<(Guid Id, string ZoneName, decimal FeeUsd, int TravelMinutes)> ServedAddressAsync(
        string email, Guid restaurantId)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        var row = await (
            from address in db.Addresses
            join zone in db.RestaurantZones
                on address.ZoneId equals zone.ZoneId
            where address.User.Email == email && zone.RestaurantId == restaurantId && zone.IsActive
            select new { address.Id, ZoneName = address.Zone.Name, zone.DeliveryFeeUsd, zone.EstimatedMinutes })
            .FirstAsync(Ct);

        return (row.Id, row.ZoneName, row.DeliveryFeeUsd, row.EstimatedMinutes);
    }

    /// <summary>An address in a zone this restaurant does not serve, if the seed has one.</summary>
    private async Task<(Guid Id, string ZoneName)?> UnservedZoneAddressAsync(string email, Guid restaurantId)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        var served = await db.RestaurantZones
            .Where(z => z.RestaurantId == restaurantId && z.IsActive)
            .Select(z => z.ZoneId)
            .ToListAsync(Ct);

        var row = await db.Addresses
            .Where(a => a.User.Email == email && !served.Contains(a.ZoneId))
            .Select(a => new { a.Id, ZoneName = a.Zone.Name })
            .FirstOrDefaultAsync(Ct);

        return row is null ? null : (row.Id, row.ZoneName);
    }

    private async Task<decimal> CurrentRateAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.ExchangeRates
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => r.RateLbpPerUsd)
            .FirstAsync(Ct);
    }
}
