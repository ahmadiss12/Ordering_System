using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderingSystem.Api.IntegrationTests.Auth;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Application.Common;
using OrderingSystem.Application.Features.Auth;
using OrderingSystem.Application.Features.Cart;
using OrderingSystem.Application.Features.Orders;
using OrderingSystem.Application.Features.Platform;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Infrastructure.Persistence.Seed;

namespace OrderingSystem.Api.IntegrationTests.Platform;

/// <summary>
/// What the platform sets, and who may set it.
///
/// <para>
/// Two fields, and both of them are somebody else's money or livelihood: the commission rate is
/// what a restaurant is charged, and the listing switch is whether it exists as far as customers
/// are concerned. So most of what is asserted here is about who is refused, and about a rate
/// change staying out of settlements that already happened.
/// </para>
/// </summary>
public sealed class PlatformRestaurantsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Admin = "admin@ordering.test";
    private const string Slug = "shawarma-station";

    // ------------------------------------------------------------------ who may look

    [Fact]
    public async Task An_admin_sees_every_restaurant_including_the_hidden_ones()
    {
        var admin = await SignInAsync(Admin);
        var target = await IdAsync(Slug);

        try
        {
            await SetListingAsync(admin, target, false);

            var listed = await ListAsync(admin);

            // The reason this list cannot be the public catalog: a hidden restaurant is invisible
            // everywhere else, so if it were missing here nothing could ever switch it back on.
            listed.ShouldContain(r => r.Id == target && !r.IsActive);
            listed.Count.ShouldBeGreaterThan(1);
        }
        finally
        {
            await SetListingAsync(admin, target, true);
        }
    }

    [Fact]
    public async Task An_owner_cannot_see_the_platform_list()
    {
        var owner = await SignInAsync("owner@shawarma.test");

        (await owner.GetAsync("/api/platform/restaurants", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_customer_cannot_see_the_platform_list()
    {
        var customer = await SignInAsync("rita@example.test");

        (await customer.GetAsync("/api/platform/restaurants", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ who may set

    [Fact]
    public async Task An_owner_cannot_set_their_own_commission()
    {
        var owner = await SignInAsync("owner@shawarma.test");
        var theirs = await IdAsync(Slug);

        // The case the endpoints were shaped around. Every other write path in the system uses a
        // guard that lets a restaurant act on itself, which is right for a menu and would be a
        // disaster here: they would be setting the rate they are charged.
        var response = await owner.PutAsJsonAsync(
            $"/api/platform/restaurants/{theirs}/commission", new SetCommissionRequest(0m), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CommissionAsync(Slug)).ShouldNotBe(0m);
    }

    [Fact]
    public async Task An_owner_cannot_list_themselves_back_on()
    {
        var admin = await SignInAsync(Admin);
        var owner = await SignInAsync("owner@shawarma.test");
        var theirs = await IdAsync(Slug);

        try
        {
            await SetListingAsync(admin, theirs, false);

            var response = await owner.PutAsJsonAsync(
                $"/api/platform/restaurants/{theirs}/listing", new SetListingRequest(true), Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await SetListingAsync(admin, theirs, true);
        }
    }

    /// <summary>
    /// The service's own guard, reached without going through the controller.
    ///
    /// <para>
    /// Every other test here is refused by the policy attribute before the service is entered, so
    /// none of them says anything about what the service itself checks. Swapping its guard for
    /// <c>EnsureCanActFor</c> — the check every other write path uses, and one that lets a
    /// restaurant act on itself — broke nothing and failed nothing. This is what makes the second
    /// layer real rather than decorative.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_service_refuses_an_owner_on_its_own_even_with_the_policy_out_of_the_way()
    {
        var restaurantId = await IdAsync(Slug);
        var before = await CommissionAsync(Slug);
        var tenant = TestTenant.Staff(Guid.NewGuid(), restaurantId);

        await using var db = factory.CreateDbContext(tenant);
        var service = new PlatformRestaurantsService(db, new TenantGuard(tenant), new NoValidation());

        // Their own restaurant's id, which is exactly what would slip past the usual guard.
        await Should.ThrowAsync<ForbiddenException>(
            async () => await service.SetCommissionAsync(restaurantId, new SetCommissionRequest(0m), Ct));

        await Should.ThrowAsync<ForbiddenException>(
            async () => await service.SetListingAsync(restaurantId, new SetListingRequest(false), Ct));

        await Should.ThrowAsync<ForbiddenException>(async () => await service.ListAsync(Ct));

        // Nothing was written. Checked rather than assumed, and restored rather than trusted: a
        // guard that stopped working would leave this restaurant on 0% commission and a listing
        // switched off, and the failures would land in the other tests with nothing pointing back
        // to this one.
        (await CommissionAsync(Slug)).ShouldBe(before);
        await RestoreAsync(restaurantId, before);
    }

    /// <summary>Puts the shared restaurant back, from the database, whatever a test did to it.</summary>
    private async Task RestoreAsync(Guid restaurantId, decimal commissionPercent)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        var restaurant = await db.Restaurants.FirstAsync(r => r.Id == restaurantId, Ct);
        restaurant.CommissionPercent = commissionPercent;
        restaurant.IsActive = true;

        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task The_service_admits_a_platform_admin_on_its_own()
    {
        var tenant = TestTenant.PlatformAdmin();

        await using var db = factory.CreateDbContext(tenant);
        var service = new PlatformRestaurantsService(db, new TenantGuard(tenant), new NoValidation());

        // The other half, so the test above cannot be passing because the service refuses
        // everybody.
        (await service.ListAsync(Ct)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_restaurant_that_does_not_exist_is_not_found()
    {
        var admin = await SignInAsync(Admin);

        var response = await admin.PutAsJsonAsync(
            $"/api/platform/restaurants/{Guid.NewGuid()}/commission",
            new SetCommissionRequest(10m), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------ commission

    [Fact]
    public async Task Changing_the_rate_does_not_touch_an_order_already_placed()
    {
        var admin = await SignInAsync(Admin);
        var restaurantId = await IdAsync(Slug);
        var before = await CommissionAsync(Slug);

        var placed = await PlaceOrderAsync();

        // Otherwise "the numbers did not change" is a claim about two zeroes.
        placed.Percent.ShouldBe(before);
        placed.Usd.ShouldBeGreaterThan(0m);

        try
        {
            await SetCommissionAsync(admin, restaurantId, before + 7m);

            // The property the whole design rests on. An order carries the rate it was placed
            // under, so a change here cannot restate a settlement that already happened - which
            // would look exactly like a bug and only be noticed at the end of the month.
            var (percent, usd) = await OrderCommissionAsync(placed);

            percent.ShouldBe(placed.Percent);
            usd.ShouldBe(placed.Usd);
        }
        finally
        {
            await SetCommissionAsync(admin, restaurantId, before);
        }
    }

    [Fact]
    public async Task The_next_order_is_charged_the_new_rate()
    {
        var admin = await SignInAsync(Admin);
        var restaurantId = await IdAsync(Slug);
        var before = await CommissionAsync(Slug);

        try
        {
            await SetCommissionAsync(admin, restaurantId, before + 7m);

            // And the other half: a change that reached nothing at all would be a setting that
            // does not do anything.
            var placed = await PlaceOrderAsync();

            placed.Percent.ShouldBe(before + 7m);
        }
        finally
        {
            await SetCommissionAsync(admin, restaurantId, before);
        }
    }

    [Fact]
    public async Task Free_of_commission_is_allowed()
    {
        var admin = await SignInAsync(Admin);
        var restaurantId = await IdAsync(Slug);
        var before = await CommissionAsync(Slug);

        try
        {
            // Zero is a real arrangement - a launch deal, a favour - and a validator that
            // insisted on a cent would make it unexpressible.
            var saved = await SetCommissionAsync(admin, restaurantId, 0m);

            saved.CommissionPercent.ShouldBe(0m);
        }
        finally
        {
            await SetCommissionAsync(admin, restaurantId, before);
        }
    }

    [Theory]
    [InlineData(-1, "a negative rate")]
    [InlineData(150, "150 typed for 15")]
    [InlineData(12.345, "a third decimal place the column would round away")]
    public async Task A_rate_that_cannot_be_meant_is_refused(decimal percent, string why)
    {
        var admin = await SignInAsync(Admin);
        var restaurantId = await IdAsync(Slug);

        var response = await admin.PutAsJsonAsync(
            $"/api/platform/restaurants/{restaurantId}/commission",
            new SetCommissionRequest(percent), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, why);
    }

    // ------------------------------------------------------------------ the listing switch

    [Fact]
    public async Task Hiding_a_restaurant_takes_it_off_the_public_catalog()
    {
        var admin = await SignInAsync(Admin);
        var restaurantId = await IdAsync(Slug);
        var anonymous = factory.CreateClient();

        try
        {
            await SetListingAsync(admin, restaurantId, false);

            (await anonymous.GetAsync($"/api/restaurants/{Slug}", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.NotFound);

            var catalog = await anonymous.GetFromJsonAsync<PagedRestaurants>("/api/restaurants", Ct);
            catalog!.Items.ShouldNotContain(r => r.Slug == Slug);
        }
        finally
        {
            await SetListingAsync(admin, restaurantId, true);
        }
    }

    [Fact]
    public async Task Hiding_a_restaurant_refuses_a_customer_mid_basket()
    {
        var admin = await SignInAsync(Admin);
        var restaurantId = await IdAsync(Slug);
        var customer = await SignInAsync("rita@example.test");

        await StockBasketAsync(customer, restaurantId);

        try
        {
            await SetListingAsync(admin, restaurantId, false);

            // Somebody with a basket already open when the switch is thrown. Following it this
            // far is the point: the switch is only meaningful in what it does to a customer.
            var checkout = await customer.PostAsJsonAsync($"/api/restaurants/{restaurantId}/orders",
                new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery,
                    null, 0m, Guid.NewGuid()), Ct);

            checkout.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            await SetListingAsync(admin, restaurantId, true);
            (await customer.DeleteAsync($"/api/restaurants/{restaurantId}/cart", Ct))
                .EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Hiding_a_restaurant_leaves_its_kitchen_working()
    {
        var admin = await SignInAsync(Admin);
        var restaurantId = await IdAsync(Slug);
        var staff = await SignInAsync("owner@shawarma.test");

        var placed = await PlaceOrderAsync();

        try
        {
            await SetListingAsync(admin, restaurantId, false);

            // People who are already waiting for food should get it, whatever the platform has
            // decided about the listing. Locking the kitchen out would strand them.
            (await staff.GetAsync("/api/restaurant/orders?page=1&pageSize=1", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.OK);

            var accepted = await staff.PostAsJsonAsync($"/api/orders/{placed.Id}/status",
                new ChangeOrderStatusRequest(OrderStatus.Accepted, null, null), Ct);

            accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await SetListingAsync(admin, restaurantId, true);
        }
    }

    [Fact]
    public async Task The_list_counts_the_people_still_waiting()
    {
        var admin = await SignInAsync(Admin);
        var restaurantId = await IdAsync(Slug);

        var before = (await ListAsync(admin)).Single(r => r.Id == restaurantId).LiveOrderCount;
        var placed = await PlaceOrderAsync();

        var after = (await ListAsync(admin)).Single(r => r.Id == restaurantId).LiveOrderCount;
        after.ShouldBe(before + 1, "a placed order is somebody waiting");

        // ...and stops counting once nobody is waiting any more. Without this the number only
        // ever grows and says nothing about right now.
        var customer = await SignInAsync("rita@example.test");
        (await customer.PostAsJsonAsync($"/api/orders/{placed.Id}/status",
            new ChangeOrderStatusRequest(OrderStatus.Cancelled, null, null), Ct))
            .EnsureSuccessStatusCode();

        (await ListAsync(admin)).Single(r => r.Id == restaurantId).LiveOrderCount.ShouldBe(before);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<IReadOnlyList<PlatformRestaurantResponse>> ListAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<List<PlatformRestaurantResponse>>("/api/platform/restaurants", Ct))!;

    private static async Task<PlatformRestaurantResponse> SetCommissionAsync(
        HttpClient admin, Guid restaurantId, decimal percent)
    {
        var response = await admin.PutAsJsonAsync(
            $"/api/platform/restaurants/{restaurantId}/commission",
            new SetCommissionRequest(percent), Ct);

        await EnsureSucceededAsync(response);
        return (await response.Content.ReadFromJsonAsync<PlatformRestaurantResponse>(Ct))!;
    }

    private static async Task SetListingAsync(HttpClient admin, Guid restaurantId, bool isActive) =>
        await EnsureSucceededAsync(await admin.PutAsJsonAsync(
            $"/api/platform/restaurants/{restaurantId}/listing", new SetListingRequest(isActive), Ct));

    private async Task<Guid> IdAsync(string slug)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Restaurants.Where(r => r.Slug == slug).Select(r => r.Id).FirstAsync(Ct);
    }

    private async Task<decimal> CommissionAsync(string slug)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Restaurants.Where(r => r.Slug == slug)
            .Select(r => r.CommissionPercent).FirstAsync(Ct);
    }

    private async Task<(decimal Percent, decimal Usd)> OrderCommissionAsync(PlacedOrder order)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var row = await db.Orders.Where(o => o.Id == order.Id)
            .Select(o => new { o.CommissionPercent, o.CommissionUsd }).FirstAsync(Ct);

        return (row.CommissionPercent, row.CommissionUsd);
    }

    private sealed record PlacedOrder(Guid Id, decimal Percent, decimal Usd);

    /// <summary>A real pickup order from the seeded restaurant, and what it was charged.</summary>
    private async Task<PlacedOrder> PlaceOrderAsync()
    {
        var customer = await SignInAsync("rita@example.test");
        var restaurantId = await IdAsync(Slug);

        await StockBasketAsync(customer, restaurantId);

        var quote = await customer.GetFromJsonAsync<QuoteResponse>(
            $"/api/restaurants/{restaurantId}/cart/quote?fulfillment=Pickup", Ct);

        var response = await customer.PostAsJsonAsync($"/api/restaurants/{restaurantId}/orders",
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery,
                null, quote!.TotalUsd, Guid.NewGuid()), Ct);

        await EnsureSucceededAsync(response);
        var placed = (await response.Content.ReadFromJsonAsync<PlacedOrderResponse>(Ct))!;

        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var charged = await db.Orders.Where(o => o.Id == placed.Id)
            .Select(o => new { o.CommissionPercent, o.CommissionUsd }).FirstAsync(Ct);

        return new PlacedOrder(placed.Id, charged.CommissionPercent, charged.CommissionUsd);
    }

    private async Task StockBasketAsync(HttpClient customer, Guid restaurantId)
    {
        (await customer.DeleteAsync($"/api/restaurants/{restaurantId}/cart", Ct))
            .EnsureSuccessStatusCode();

        Guid itemId;
        List<ChosenOptionRequest> choices;

        await using (var db = factory.CreateDbContext(TestTenant.PlatformAdmin()))
        {
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

            itemId = item.Id;
            choices = [.. item.Required.Select(id => new ChosenOptionRequest(id, 1))];
        }

        await EnsureSucceededAsync(await customer.PostAsJsonAsync(
            $"/api/restaurants/{restaurantId}/cart/lines",
            new AddCartLineRequest(itemId, 2, null, choices), Ct));
    }

    private async Task<HttpClient> SignInAsync(string email)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, DatabaseSeeder.SeedPassword), Ct);
        await EnsureSucceededAsync(response);

        var tokens = await response.Content.ReadFromJsonAsync<AuthTokensResponse>(Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        return client;
    }

    private static async Task EnsureSucceededAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(Ct);
        throw new InvalidOperationException(
            $"{(int)response.StatusCode} {response.StatusCode} from "
            + $"{response.RequestMessage?.RequestUri}: {body}");
    }

    /// <summary>
    /// Validation is not what those two tests are about, and wiring FluentValidation up by hand
    /// would only add a way for them to fail for an unrelated reason.
    /// </summary>
    private sealed class NoValidation : IValidationService
    {
        public Task ValidateAsync<T>(T instance, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed record PagedRestaurants(List<CatalogRow> Items);
    private sealed record CatalogRow(string Slug);
}
