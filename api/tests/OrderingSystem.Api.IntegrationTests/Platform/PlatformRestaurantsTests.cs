using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
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

    // ------------------------------------------------------------------ taking one on

    [Fact]
    public async Task A_new_restaurant_arrives_hidden_with_nothing_set_up()
    {
        var admin = await SignInAsync(Admin);
        var name = Unique("Zaatar Express");

        try
        {
            var created = await CreateAsync(admin, name);

            // Exactly the state its owner has to configure their way out of. The platform
            // guessing at hours or a delivery area would be worse than an empty screen, and a
            // restaurant visible to customers before either exists would take orders it cannot
            // cook or carry.
            created.Restaurant.IsActive.ShouldBeFalse();
            created.Restaurant.LiveOrderCount.ShouldBe(0);

            var (hours, zones, items) = await ConfigurationCountsAsync(created.Restaurant.Slug);
            hours.ShouldBe(0);
            zones.ShouldBe(0);
            items.ShouldBe(0);

            // The kitchen's own switch starts on. Starting it paused would leave an owner
            // hunting for which of two switches was keeping them shut.
            created.Restaurant.IsAcceptingOrders.ShouldBeTrue();
        }
        finally
        {
            await ForgetRestaurantAsync(name);
        }
    }

    [Fact]
    public async Task A_new_restaurant_gets_an_owner_who_can_sign_in_and_configure_it()
    {
        var admin = await SignInAsync(Admin);
        var name = Unique("Manoushe House");
        var ownerEmail = $"owner-{Guid.NewGuid():N}@example.test";

        try
        {
            var created = await CreateAsync(admin, name, ownerEmail);
            created.InvitationEmailed.ShouldBeTrue();

            // The whole point of creating it: somebody can now set it up. Following the emailed
            // link the way they would.
            var owner = await AcceptInvitationAsync(ownerEmail);

            (await owner.GetAsync("/api/restaurant/hours", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await owner.GetAsync("/api/restaurant/staff", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.OK, "the first owner has to be able to hire");
        }
        finally
        {
            await ForgetRestaurantAsync(name);
            await ForgetUserAsync(ownerEmail);
        }
    }

    [Fact]
    public async Task A_new_restaurant_is_invisible_to_customers_until_it_is_listed()
    {
        var admin = await SignInAsync(Admin);
        var name = Unique("Falafel Lane");
        var anonymous = factory.CreateClient();

        try
        {
            var created = await CreateAsync(admin, name);

            (await anonymous.GetAsync($"/api/restaurants/{created.Restaurant.Slug}", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.NotFound);

            // ...and visible the moment the platform says so, which is the other half of the
            // switch meaning anything.
            await SetListingAsync(admin, created.Restaurant.Id, true);

            (await anonymous.GetAsync($"/api/restaurants/{created.Restaurant.Slug}", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await ForgetRestaurantAsync(name);
        }
    }

    [Fact]
    public async Task A_link_is_made_from_the_name_when_none_is_given()
    {
        var admin = await SignInAsync(Admin);
        var name = Unique("Café  Beirut & Sons");

        try
        {
            var created = await CreateAsync(admin, name);

            // Accents dropped, punctuation and doubled spaces collapsed to single hyphens. An
            // address bar reading "caf%C3%A9--beirut-&-sons" is nobody's idea of a tidy link.
            created.Restaurant.Slug.ShouldStartWith("cafe-beirut-sons-");
            created.Restaurant.Slug.ShouldMatch("^[a-z0-9]+(-[a-z0-9]+)*$");
        }
        finally
        {
            await ForgetRestaurantAsync(name);
        }
    }

    [Fact]
    public async Task A_link_that_is_already_taken_is_refused_rather_than_numbered()
    {
        var admin = await SignInAsync(Admin);

        var response = await admin.PostAsJsonAsync("/api/platform/restaurants",
            NewRestaurant("Shawarma Station Two", slug: Slug), Ct);

        // Not "shawarma-station-2". That is a link somebody has to live with forever, chosen by a
        // computer in a moment nobody was watching.
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain(Slug);
    }

    [Fact]
    public async Task A_name_no_link_can_be_made_from_asks_for_one()
    {
        var admin = await SignInAsync(Admin);

        // Arabic, which the slug rules cannot turn into anything. Inventing a transliteration
        // would saddle the restaurant with a link somebody else guessed at.
        var response = await admin.PostAsJsonAsync("/api/platform/restaurants",
            NewRestaurant("مطعم الشام"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Type the one");
    }

    [Fact]
    public async Task An_owner_who_already_runs_a_restaurant_cannot_be_given_a_second()
    {
        var admin = await SignInAsync(Admin);

        var response = await admin.PostAsJsonAsync("/api/platform/restaurants",
            NewRestaurant(Unique("Second Kitchen"), ownerEmail: "owner@frieslab.test"), Ct);

        // Same rule as an ordinary invitation, and for the same reason: a token carries one
        // restaurant and nothing lets its holder choose which.
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("another restaurant");
    }

    [Fact]
    public async Task An_owner_cannot_create_a_restaurant()
    {
        var owner = await SignInAsync("owner@shawarma.test");

        var response = await owner.PostAsJsonAsync("/api/platform/restaurants",
            NewRestaurant(Unique("My Second Place")), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("", "no name")]
    [InlineData("Fine Name", "a link with spaces in it")]
    public async Task A_restaurant_that_could_not_work_is_refused(string name, string why)
    {
        var admin = await SignInAsync(Admin);

        var request = why == "no name"
            ? NewRestaurant(name)
            : NewRestaurant(name, slug: "not a slug");

        var response = await admin.PostAsJsonAsync("/api/platform/restaurants", request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, why);
    }

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
        var service = new PlatformRestaurantsService(
            db, new TenantGuard(tenant), new NoValidation(), null!, null!);

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
        var service = new PlatformRestaurantsService(
            db, new TenantGuard(tenant), new NoValidation(), null!, null!);

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

    private async Task<HttpClient> SignInAsync(string email, string? password = null)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password ?? DatabaseSeeder.SeedPassword), Ct);
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
    ///
    /// <para>
    /// The clock and the invitation machinery are passed as null for a related but sharper
    /// reason: the guard has to throw before either is touched. If it ever stopped doing so, the
    /// test fails on a null reference rather than quietly passing — which is the outcome wanted
    /// from a check whose whole job is to run first.
    /// </para>
    /// </summary>
    private sealed class NoValidation : IValidationService
    {
        public Task ValidateAsync<T>(T instance, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>A name no other run will collide with, since these tests share one database.</summary>
    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..28];

    private static CreateRestaurantRequest NewRestaurant(
        string name, string? slug = null, string? ownerEmail = null) =>
        new(
            name,
            slug,
            "+96170123456",
            15m,
            ownerEmail ?? $"owner-{Guid.NewGuid():N}@example.test",
            "New Owner",
            null);

    private static async Task<CreatedRestaurantResponse> CreateAsync(
        HttpClient admin, string name, string? ownerEmail = null)
    {
        var response = await admin.PostAsJsonAsync("/api/platform/restaurants",
            NewRestaurant(name, ownerEmail: ownerEmail), Ct);

        await EnsureSucceededAsync(response);
        return (await response.Content.ReadFromJsonAsync<CreatedRestaurantResponse>(Ct))!;
    }

    private async Task<(int Hours, int Zones, int Items)> ConfigurationCountsAsync(string slug)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        return await db.Restaurants
            .Where(r => r.Slug == slug)
            .Select(r => new ValueTuple<int, int, int>(r.Hours.Count, r.Zones.Count, r.MenuItems.Count))
            .FirstAsync(Ct);
    }

    /// <summary>Follows the emailed link, chooses a password, and signs in with it.</summary>
    private async Task<HttpClient> AcceptInvitationAsync(string email)
    {
        var body = factory.Emails.Sent.Last(m => m.To == email).Body;
        var match = Regex.Match(body, @"token=([A-Za-z0-9_\-%]+)", RegexOptions.None, TimeSpan.FromSeconds(1));
        match.Success.ShouldBeTrue("the invitation must carry a link");

        await EnsureSucceededAsync(await factory.CreateClient().PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest(Uri.UnescapeDataString(match.Groups[1].Value), "Chosen-Passw0rd"), Ct));

        return await SignInAsync(email, "Chosen-Passw0rd");
    }

    /// <summary>
    /// Removes a restaurant a test created, straight from the database.
    ///
    /// <para>
    /// There is no way to delete one through the product, deliberately — a restaurant with orders
    /// against it must keep resolving them. These have none, and leaving them would grow the
    /// platform list every run and change what the tests above are counting.
    /// </para>
    /// </summary>
    private async Task ForgetRestaurantAsync(string name)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        var ids = await db.Restaurants.Where(r => r.Name == name).Select(r => r.Id).ToListAsync(Ct);
        if (ids.Count == 0)
        {
            return;
        }

        var staff = await db.RestaurantStaff.IgnoreQueryFilters()
            .Where(s => ids.Contains(s.RestaurantId))
            .Select(s => s.UserId)
            .ToListAsync(Ct);

        await db.RestaurantStaff.IgnoreQueryFilters()
            .Where(s => ids.Contains(s.RestaurantId)).ExecuteDeleteAsync(Ct);
        await db.Restaurants.Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync(Ct);

        foreach (var userId in staff)
        {
            await db.UserRoles.Where(r => r.UserId == userId).ExecuteDeleteAsync(Ct);
            await db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(Ct);
            await db.PasswordResetTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(Ct);

            if (!await db.Orders.IgnoreQueryFilters().AnyAsync(o => o.CustomerId == userId, Ct))
            {
                await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(Ct);
            }
        }
    }

    private async Task ForgetUserAsync(string email)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        await db.Users.Where(u => u.Email == email).ExecuteDeleteAsync(Ct);
    }

    private sealed record PagedRestaurants(List<CatalogRow> Items);
    private sealed record CatalogRow(string Slug);
}
