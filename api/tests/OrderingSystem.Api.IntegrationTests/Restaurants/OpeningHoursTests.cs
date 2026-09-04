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
/// A restaurant setting its own opening hours.
///
/// <para>
/// These rows decide whether the checkout takes an order at all, which is what separates this
/// from the rest of the settings: a mistake here does not look like a mistake, it looks like a
/// quiet evening. So the refusals matter more than the saves.
/// </para>
/// </summary>
public sealed class OpeningHoursTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Mezze House, so FriesLab's window stays untouched for the tests that rely on it.</summary>
    private const string Owner = "owner@mezze.test";
    private const string Slug = "beirut-mezze-house";

    // ------------------------------------------------------------------ reading

    [Fact]
    public async Task Staff_can_see_the_week_and_whether_it_is_open_right_now()
    {
        var owner = await SignInAsync(Owner);

        var week = await owner.GetFromJsonAsync<WeeklyHoursResponse>("/api/restaurant/hours", Ct);

        // The seeded mezze house shuts between lunch and dinner: two windows a day, seven days.
        week!.Windows.Count.ShouldBe(14);
        week.IsClosedIndefinitely.ShouldBeFalse();
    }

    [Fact]
    public async Task The_week_comes_back_monday_first()
    {
        var owner = await SignInAsync(Owner);

        var week = await owner.GetFromJsonAsync<WeeklyHoursResponse>("/api/restaurant/hours", Ct);

        // A week of opening hours is read starting on Monday, not on Sunday as DayOfWeek numbers
        // it. Sorting by the raw enum would put Sunday at the top of the screen.
        week!.Windows[0].Day.ShouldBe(DayOfWeek.Monday);
        week.Windows[^1].Day.ShouldBe(DayOfWeek.Sunday);
    }

    // ------------------------------------------------------------------ writing

    [Fact]
    public async Task An_owner_can_replace_the_week()
    {
        var owner = await SignInAsync(Owner);
        var before = await WeekAsync(owner);

        try
        {
            var week = await SetAsync(owner, [Window(DayOfWeek.Monday, 9, 0, 17, 0)]);

            week.Windows.ShouldHaveSingleItem();
            week.Windows[0].OpenTime.ShouldBe(new TimeOnly(9, 0));
            week.IsClosedIndefinitely.ShouldBeFalse();
        }
        finally
        {
            await SetAsync(owner, [.. before.Windows]);
        }
    }

    [Fact]
    public async Task Staff_cannot_change_the_hours()
    {
        var staff = await SignInAsync("staff@frieslab.test");

        var response = await staff.PutAsJsonAsync("/api/restaurant/hours",
            new SetWeeklyHoursRequest([Window(DayOfWeek.Monday, 9, 0, 17, 0)], false), Ct);

        // These decide whether the restaurant earns anything tomorrow.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_overnight_window_is_one_row_and_survives_the_round_trip()
    {
        var owner = await SignInAsync(Owner);
        var before = await WeekAsync(owner);

        try
        {
            // Noon until two in the morning. A close time earlier than the open time is how the
            // domain has stored this since Phase 2, and an editor has to be able to say it.
            var week = await SetAsync(owner, [Window(DayOfWeek.Friday, 12, 0, 2, 0)]);

            var saved = week.Windows.ShouldHaveSingleItem();
            saved.OpenTime.ShouldBe(new TimeOnly(12, 0));
            saved.CloseTime.ShouldBe(new TimeOnly(2, 0));
        }
        finally
        {
            await SetAsync(owner, [.. before.Windows]);
        }
    }

    // ------------------------------------------------------------------ the refusals

    [Fact]
    public async Task Two_windows_covering_the_same_time_are_refused_by_name()
    {
        var owner = await SignInAsync(Owner);

        var response = await owner.PutAsJsonAsync("/api/restaurant/hours",
            new SetWeeklyHoursRequest(
                [Window(DayOfWeek.Monday, 12, 0, 16, 0), Window(DayOfWeek.Monday, 14, 0, 20, 0)],
                false), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Named, because "two windows overlap" on a screen with fourteen of them is a hunt.
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldContain("Monday 12:00");
        body.ShouldContain("Monday 14:00");
    }

    [Fact]
    public async Task A_late_window_clashing_with_the_next_morning_is_refused()
    {
        var owner = await SignInAsync(Owner);

        var response = await owner.PutAsJsonAsync("/api/restaurant/hours",
            new SetWeeklyHoursRequest(
                [Window(DayOfWeek.Monday, 18, 0, 2, 0), Window(DayOfWeek.Tuesday, 1, 0, 5, 0)],
                false), Ct);

        // The case a day-by-day check cannot see.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_window_that_opens_and_closes_at_the_same_moment_is_refused()
    {
        var owner = await SignInAsync(Owner);

        var response = await owner.PutAsJsonAsync("/api/restaurant/hours",
            new SetWeeklyHoursRequest([Window(DayOfWeek.Monday, 9, 0, 9, 0)], false), Ct);

        // The domain reads a zero-length window as closed. Saved silently, this restaurant would
        // be shut on Mondays and shown hours that say otherwise.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Emptying_the_week_needs_saying_so()
    {
        var owner = await SignInAsync(Owner);

        var response = await owner.PutAsJsonAsync("/api/restaurant/hours",
            new SetWeeklyHoursRequest([], false), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("closes you to customers");
    }

    [Fact]
    public async Task Closing_indefinitely_is_allowed_when_it_is_meant()
    {
        var owner = await SignInAsync(Owner);
        var before = await WeekAsync(owner);

        try
        {
            // A kitchen closing for August. Legitimate, and different from a half-finished edit
            // only in that somebody said so.
            var week = await SetAsync(owner, [], confirmClosed: true);

            week.Windows.ShouldBeEmpty();
            week.IsClosedIndefinitely.ShouldBeTrue();
            week.IsOpenNow.ShouldBeFalse();
        }
        finally
        {
            await SetAsync(owner, [.. before.Windows]);
        }
    }

    [Fact]
    public async Task A_day_cannot_hold_more_windows_than_a_kitchen_would_ever_file()
    {
        var owner = await SignInAsync(Owner);

        var tooMany = Enumerable.Range(0, 5)
            .Select(i => Window(DayOfWeek.Monday, 1 + (i * 4), 0, 3 + (i * 4), 0))
            .ToList();

        var response = await owner.PutAsJsonAsync("/api/restaurant/hours",
            new SetWeeklyHoursRequest(tooMany, false), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------ what the hours decide

    [Fact]
    public async Task Closing_the_restaurant_refuses_the_next_order()
    {
        var owner = await SignInAsync(Owner);
        var customer = await SignInAsync("rita@example.test");
        var restaurantId = await RestaurantIdAsync();
        var before = await WeekAsync(owner);

        await StockBasketAsync(customer, restaurantId);

        try
        {
            await SetAsync(owner, [], confirmClosed: true);

            var refused = await CheckoutAsync(customer, restaurantId);

            // The whole reason this screen is worth guarding: these rows are read by the checkout
            // on every order, and a mistake here looks like a quiet evening rather than an error.
            refused.Status.ShouldBe(HttpStatusCode.Conflict);
            refused.Body.ShouldContain("closed");
        }
        finally
        {
            await SetAsync(owner, [.. before.Windows]);
            await ClearBasketAsync(customer, restaurantId);
        }
    }

    [Fact]
    public async Task One_restaurant_cannot_set_anothers_hours()
    {
        var mezze = await SignInAsync(Owner);
        var friesLab = await SignInAsync("owner@frieslab.test");
        var friesLabBefore = await WeekAsync(friesLab);
        var mezzeBefore = await WeekAsync(mezze);

        try
        {
            // No id in the route to tamper with, so the only way to try is to send one
            // restaurant's week as the other owner and see whose rows moved.
            await SetAsync(mezze, [Window(DayOfWeek.Monday, 9, 0, 10, 0)]);

            var friesLabNow = await WeekAsync(friesLab);
            friesLabNow.Windows.Count.ShouldBe(friesLabBefore.Windows.Count);
        }
        finally
        {
            await SetAsync(mezze, [.. mezzeBefore.Windows]);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static OpeningWindow Window(
        DayOfWeek day, int openHour, int openMinute, int closeHour, int closeMinute) =>
        new(day, new TimeOnly(openHour, openMinute), new TimeOnly(closeHour, closeMinute));

    private static async Task<WeeklyHoursResponse> WeekAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<WeeklyHoursResponse>("/api/restaurant/hours", Ct))!;

    private static async Task<WeeklyHoursResponse> SetAsync(
        HttpClient client, IReadOnlyList<OpeningWindow> windows, bool confirmClosed = false)
    {
        var response = await client.PutAsJsonAsync("/api/restaurant/hours",
            new SetWeeklyHoursRequest(windows, confirmClosed), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"saving hours failed with {(int)response.StatusCode}: {body}");

        return (await response.Content.ReadFromJsonAsync<WeeklyHoursResponse>(Ct))!;
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

    private async Task<Guid> RestaurantIdAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Restaurants.Where(r => r.Slug == Slug).Select(r => r.Id).FirstAsync(Ct);
    }

    /// <summary>
    /// The priciest item, with one choice picked from every group that demands one.
    ///
    /// <para>
    /// Priciest so the minimum order is never what refuses the checkout. The choices are supplied
    /// rather than avoided because every dish this restaurant sells asks which bread you want —
    /// the first version of this hunted for a choice-free item and found none, which is a fair
    /// description of a mezze house.
    /// </para>
    /// </summary>
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
