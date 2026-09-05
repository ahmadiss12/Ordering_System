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

namespace OrderingSystem.Api.IntegrationTests.Restaurants;

/// <summary>
/// Where a restaurant delivers, and what changing that does to a customer.
///
/// <para>
/// A fee is what somebody pays and a zone is whether they can order at all, so the tests that
/// matter here are the ones that follow a change through to a checkout — not the ones that prove
/// a number saved.
/// </para>
/// </summary>
public sealed class RestaurantZonesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Mezze House, so FriesLab's seeded coverage stays as the other suites expect it.</summary>
    private const string Owner = "owner@mezze.test";
    private const string Slug = "beirut-mezze-house";

    // ------------------------------------------------------------------ reading

    [Fact]
    public async Task Every_platform_zone_is_listed_whether_or_not_it_is_served()
    {
        var owner = await SignInAsync(Owner);

        var zones = await ZonesAsync(owner);

        // A restaurant cannot pick a zone it does not know exists, and a list of only the
        // configured ones would make adding the first one impossible to find.
        zones.Count.ShouldBeGreaterThan(zones.Count(z => z.IsServed));
        zones.ShouldContain(z => !z.IsServed);
    }

    [Fact]
    public async Task An_unserved_zone_has_no_remembered_terms()
    {
        var owner = await SignInAsync(Owner);

        var untouched = (await ZonesAsync(owner)).First(z => !z.IsServed);

        // Null rather than zero: "we have never set terms for Jounieh" and "we deliver to Jounieh
        // for nothing" are different answers.
        untouched.DeliveryFeeUsd.ShouldBeNull();
        untouched.EstimatedMinutes.ShouldBeNull();
    }

    [Fact]
    public async Task A_customer_cannot_read_a_restaurants_zones()
    {
        var customer = await SignInAsync("rita@example.test");

        (await customer.GetAsync("/api/restaurant/zones", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ writing

    [Fact]
    public async Task An_owner_can_start_serving_a_new_zone()
    {
        var owner = await SignInAsync(Owner);
        var zone = (await ZonesAsync(owner)).First(z => !z.IsServed);

        try
        {
            var saved = await SetAsync(owner, zone.ZoneId, new SetRestaurantZoneRequest(true, 3.5m, 25));

            saved.IsServed.ShouldBeTrue();
            saved.DeliveryFeeUsd.ShouldBe(3.5m);

            var listed = (await ZonesAsync(owner)).Single(z => z.ZoneId == zone.ZoneId);
            listed.IsServed.ShouldBeTrue();
            listed.EstimatedMinutes.ShouldBe(25);

            // Restoring here rather than only in the finally, so the cleanup itself is asserted.
            // A restore that left the zone unserved-but-with-terms would poison whichever test
            // ran next looking for an untouched zone, and the failure would land over there,
            // intermittently, with nothing to point back at this test.
            await RestoreAsync(owner, zone);

            var restored = (await ZonesAsync(owner)).Single(z => z.ZoneId == zone.ZoneId);
            restored.IsServed.ShouldBeFalse();
            restored.DeliveryFeeUsd.ShouldBeNull();
            restored.EstimatedMinutes.ShouldBeNull();
        }
        finally
        {
            await RestoreAsync(owner, zone);
        }
    }

    [Fact]
    public async Task Suspending_a_zone_keeps_its_fee_for_when_it_comes_back()
    {
        var owner = await SignInAsync(Owner);
        var zone = (await ZonesAsync(owner)).First(z => z.IsServed);

        try
        {
            var suspended = await SetAsync(owner, zone.ZoneId,
                new SetRestaurantZoneRequest(false, zone.DeliveryFeeUsd!.Value, zone.EstimatedMinutes!.Value));

            suspended.IsServed.ShouldBeFalse();

            // The row survives with its numbers, which is what the IsActive column is for: a
            // fortnight's suspension should not cost a re-entry.
            var listed = (await ZonesAsync(owner)).Single(z => z.ZoneId == zone.ZoneId);
            listed.IsServed.ShouldBeFalse();
            listed.DeliveryFeeUsd.ShouldBe(zone.DeliveryFeeUsd);
            listed.EstimatedMinutes.ShouldBe(zone.EstimatedMinutes);
        }
        finally
        {
            await RestoreAsync(owner, zone);
        }
    }

    [Fact]
    public async Task Staff_cannot_change_a_fee()
    {
        var staff = await SignInAsync("staff@frieslab.test");
        var zone = (await ZonesAsync(staff))[0];

        var response = await staff.PutAsJsonAsync($"/api/restaurant/zones/{zone.ZoneId}",
            new SetRestaurantZoneRequest(true, 0m, 10), Ct);

        // A delivery fee is what a customer pays.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_zone_the_platform_does_not_have_is_refused()
    {
        var owner = await SignInAsync(Owner);

        var response = await owner.PutAsJsonAsync($"/api/restaurant/zones/{Guid.NewGuid()}",
            new SetRestaurantZoneRequest(true, 3m, 20), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(-1, 20, "a negative fee")]
    [InlineData(5000, 20, "a mistyped fee")]
    [InlineData(3, 0, "no travel time at all")]
    [InlineData(3, 600, "ten hours of driving")]
    public async Task Nonsense_terms_are_refused(decimal fee, int minutes, string why)
    {
        var owner = await SignInAsync(Owner);
        var zone = (await ZonesAsync(owner))[0];

        var response = await owner.PutAsJsonAsync($"/api/restaurant/zones/{zone.ZoneId}",
            new SetRestaurantZoneRequest(true, fee, minutes), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, why);
    }

    [Fact]
    public async Task Free_delivery_is_allowed()
    {
        var owner = await SignInAsync(Owner);
        var zone = (await ZonesAsync(owner)).First(z => z.IsServed);

        try
        {
            // Zero is a real offer, not an oversight. A restaurant wanting to make it should not
            // have to charge a cent.
            var saved = await SetAsync(owner, zone.ZoneId, new SetRestaurantZoneRequest(true, 0m, 15));

            saved.DeliveryFeeUsd.ShouldBe(0m);
        }
        finally
        {
            await RestoreAsync(owner, zone);
        }
    }

    // ------------------------------------------------------------------ what a change does

    [Fact]
    public async Task Suspending_a_zone_refuses_a_customer_who_lives_there()
    {
        var owner = await SignInAsync(Owner);
        var customer = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();

        var (addressId, zoneId, zoneName) = await ServedAddressAsync("rita@example.test", restaurantId);
        var terms = (await ZonesAsync(owner)).Single(z => z.ZoneId == zoneId);
        var before = new SetRestaurantZoneRequest(true, terms.DeliveryFeeUsd!.Value, terms.EstimatedMinutes!.Value);

        await StockBasketAsync(customer, restaurantId);

        try
        {
            await SetAsync(owner, zoneId, before with { IsServed = false });

            var refused = await CheckoutAsync(customer, restaurantId, addressId);

            // The case worth following all the way through: a saved address the restaurant has
            // stopped covering, and a refusal that names the place rather than saying "no".
            refused.Status.ShouldBe(HttpStatusCode.Conflict);
            refused.Body.ShouldContain(zoneName);
        }
        finally
        {
            await RestoreAsync(owner, terms);
            await ClearBasketAsync(customer, restaurantId);
        }
    }

    [Fact]
    public async Task Raising_a_fee_changes_the_next_quote_and_no_placed_order()
    {
        var owner = await SignInAsync(Owner);
        var customer = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();

        var (addressId, zoneId, _) = await ServedAddressAsync("rita@example.test", restaurantId);
        var terms = (await ZonesAsync(owner)).Single(z => z.ZoneId == zoneId);
        var before = new SetRestaurantZoneRequest(true, terms.DeliveryFeeUsd!.Value, terms.EstimatedMinutes!.Value);

        var placed = await PlaceOrderAsync(customer, restaurantId, addressId);

        try
        {
            await SetAsync(owner, zoneId, before with { DeliveryFeeUsd = before.DeliveryFeeUsd + 5m });

            await StockBasketAsync(customer, restaurantId);
            var quote = await QuoteAsync(customer, restaurantId, addressId);
            quote.DeliveryFeeUsd.ShouldBe(before.DeliveryFeeUsd + 5m);

            // And the order already placed still says what it charged. The snapshot property
            // again, from a different direction.
            var after = await customer.GetFromJsonAsync<OrderDetailResponse>(
                $"/api/orders/{placed.Id}", Ct);
            after!.DeliveryFeeUsd.ShouldBe(placed.DeliveryFeeUsd);
            after.TotalUsd.ShouldBe(placed.TotalUsd);
        }
        finally
        {
            await RestoreAsync(owner, terms);
            await ClearBasketAsync(customer, restaurantId);
        }
    }

    [Fact]
    public async Task One_restaurant_cannot_set_anothers_terms()
    {
        var mezze = await SignInAsync(Owner);
        var friesLab = await SignInAsync("owner@frieslab.test");

        var friesLabBefore = await ZonesAsync(friesLab);
        var zone = friesLabBefore.First(z => z.IsServed);
        var mezzeTerms = (await ZonesAsync(mezze)).Single(z => z.ZoneId == zone.ZoneId);

        try
        {
            // The zone id is in the route, but the restaurant id is not — it comes from the token,
            // so the worst this can do is edit the caller's own terms for a shared zone.
            await SetAsync(mezze, zone.ZoneId, new SetRestaurantZoneRequest(true, 49m, 179));

            var friesLabNow = (await ZonesAsync(friesLab)).Single(z => z.ZoneId == zone.ZoneId);
            friesLabNow.DeliveryFeeUsd.ShouldBe(zone.DeliveryFeeUsd);
            friesLabNow.EstimatedMinutes.ShouldBe(zone.EstimatedMinutes);
        }
        finally
        {
            await RestoreAsync(mezze, mezzeTerms);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<IReadOnlyList<RestaurantZoneResponse>> ZonesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<RestaurantZoneResponse>>("/api/restaurant/zones", Ct))!;

    private static async Task<RestaurantZoneResponse> SetAsync(
        HttpClient client, Guid zoneId, SetRestaurantZoneRequest request)
    {
        var response = await client.PutAsJsonAsync($"/api/restaurant/zones/{zoneId}", request, Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"saving the zone failed with {(int)response.StatusCode}: {body}");

        return (await response.Content.ReadFromJsonAsync<RestaurantZoneResponse>(Ct))!;
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

        var (itemId, choices) = await ItemAsync();
        var added = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/cart/lines",
            new AddCartLineRequest(itemId, 2, null, choices), Ct);

        added.IsSuccessStatusCode.ShouldBeTrue(await added.Content.ReadAsStringAsync(Ct));
    }

    private static async Task ClearBasketAsync(HttpClient client, Guid restaurantId) =>
        (await client.DeleteAsync($"/api/restaurants/{restaurantId}/cart", Ct)).EnsureSuccessStatusCode();

    private static async Task<QuoteResponse> QuoteAsync(
        HttpClient client, Guid restaurantId, Guid addressId) =>
        (await client.GetFromJsonAsync<QuoteResponse>(
            $"/api/restaurants/{restaurantId}/cart/quote?fulfillment=Delivery&addressId={addressId}", Ct))!;

    private static async Task<(HttpStatusCode Status, string Body)> CheckoutAsync(
        HttpClient client, Guid restaurantId, Guid addressId)
    {
        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/orders",
            new CheckoutRequest(FulfillmentType.Delivery, addressId, PaymentMethod.CashOnDelivery,
                null, 0m, Guid.NewGuid()), Ct);

        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<PlacedOrderResponse> PlaceOrderAsync(
        HttpClient client, Guid restaurantId, Guid addressId)
    {
        await StockBasketAsync(client, restaurantId);
        var quote = await QuoteAsync(client, restaurantId, addressId);

        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/orders",
            new CheckoutRequest(FulfillmentType.Delivery, addressId, PaymentMethod.CashOnDelivery,
                null, quote.TotalUsd, Guid.NewGuid()), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"checkout failed: {body}");

        return (await response.Content.ReadFromJsonAsync<PlacedOrderResponse>(Ct))!;
    }

    /// <summary>
    /// Puts a zone back exactly as it was found, including the case of not existing.
    ///
    /// <para>
    /// Suspending is not a restore. The API deliberately keeps a suspended zone's fee, so there is
    /// no request that returns a never-configured zone to having no terms — only deleting the row
    /// does that. A <c>finally</c> that suspended with a placeholder fee instead left an unserved
    /// zone carrying terms of $0.00, and then whichever test ran next looking for an untouched
    /// zone could pick that one and fail. Passing was a matter of which order xUnit chose.
    /// </para>
    /// </summary>
    private async Task RestoreAsync(HttpClient owner, RestaurantZoneResponse before)
    {
        if (before.DeliveryFeeUsd is { } fee && before.EstimatedMinutes is { } minutes)
        {
            await SetAsync(owner, before.ZoneId, new SetRestaurantZoneRequest(before.IsServed, fee, minutes));
            return;
        }

        var restaurantId = await RestaurantIdAsync();
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        await db.RestaurantZones
            .Where(z => z.RestaurantId == restaurantId && z.ZoneId == before.ZoneId)
            .ExecuteDeleteAsync(Ct);
    }

    private async Task<Guid> RestaurantIdAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Restaurants.Where(r => r.Slug == Slug).Select(r => r.Id).FirstAsync(Ct);
    }

    private async Task<(Guid AddressId, Guid ZoneId, string ZoneName)> ServedAddressAsync(
        string email, Guid restaurantId)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        var row = await (
            from address in db.Addresses
            join zone in db.RestaurantZones on address.ZoneId equals zone.ZoneId
            where address.User.Email == email && zone.RestaurantId == restaurantId && zone.IsActive
            select new { address.Id, address.ZoneId, ZoneName = address.Zone.Name })
            .FirstAsync(Ct);

        return (row.Id, row.ZoneId, row.ZoneName);
    }

    /// <summary>The priciest dish, with a choice from every group that demands one.</summary>
    private async Task<(Guid ItemId, List<ChosenOptionRequest> Choices)> ItemAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        var item = await db.MenuItems
            .Where(i => i.Restaurant.Slug == Slug)
            .OrderByDescending(i => i.BasePriceUsd)
            .Select(i => new
            {
                i.Id,
                Required = i.OptionGroups
                    .Where(g => (g.MinSelectOverride ?? g.OptionGroup.MinSelect) > 0)
                    .Select(g => g.OptionGroup.Options.OrderBy(o => o.SortOrder).First().Id)
                    .ToList(),
            })
            .FirstAsync(Ct);

        return (item.Id, [.. item.Required.Select(id => new ChosenOptionRequest(id, 1))]);
    }
}
