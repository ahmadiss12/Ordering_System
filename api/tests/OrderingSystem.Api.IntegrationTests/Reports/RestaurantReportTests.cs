using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderingSystem.Api.IntegrationTests.Auth;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Common;
using OrderingSystem.Application.Features.Auth;
using OrderingSystem.Application.Features.Orders;
using OrderingSystem.Application.Features.Reports;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Orders;
using OrderingSystem.Infrastructure.Persistence.Seed;

namespace OrderingSystem.Api.IntegrationTests.Reports;

/// <summary>
/// What the rejection reasons were collected for.
///
/// <para>
/// The orders are written straight to the database rather than placed through checkout. A report
/// is about days that have already happened, and checkout can only ever produce today — proving
/// that a week groups correctly needs a week to exist.
/// </para>
/// <para>
/// Every test works in its own window of dates, far enough back that nothing else in the suite
/// reaches it. The restaurant is shared; the days are not.
/// </para>
/// </summary>
public sealed class RestaurantReportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Owner = "owner@shawarma.test";
    private const string Slug = "shawarma-station";

    /// <summary>Well before anything the rest of the suite places, so the counts are only ours.</summary>
    private static readonly DateOnly Window = new(2024, 3, 4);

    [Fact]
    public async Task Revenue_counts_what_was_kept_and_not_what_was_refused()
    {
        var owner = await SignInAsync(Owner);
        var day = Window;

        await using var seeded = await SeedAsync(
            Order(day, OrderStatus.Delivered, 30m, 4.5m),
            Order(day, OrderStatus.Preparing, 20m, 3m),
            Order(day, OrderStatus.Rejected, 100m, 15m, RejectionReason.OutOfStock),
            Order(day, OrderStatus.Cancelled, 50m, 7.5m));

        var report = await ReportAsync(owner, day, day);

        report.Totals.Orders.ShouldBe(4);
        report.Totals.Kept.ShouldBe(2);
        report.Totals.Rejected.ShouldBe(1);
        report.Totals.Cancelled.ShouldBe(1);

        // The 100 and the 50 are not sales. Counting them would tell a restaurant it had its best
        // day ever on the day it turned everything away.
        report.Totals.RevenueUsd.ShouldBe(50m);
        report.Totals.CommissionUsd.ShouldBe(7.5m);
    }

    [Fact]
    public async Task An_order_still_cooking_is_already_revenue()
    {
        var owner = await SignInAsync(Owner);
        var day = Window.AddDays(1);

        await using var seeded = await SeedAsync(Order(day, OrderStatus.Preparing, 25m, 3m));

        // A report that waited for delivery would read as empty right through service, which is
        // exactly when somebody looks at it.
        (await ReportAsync(owner, day, day)).Totals.RevenueUsd.ShouldBe(25m);
    }

    [Fact]
    public async Task The_average_is_over_the_orders_that_were_kept()
    {
        var owner = await SignInAsync(Owner);
        var day = Window.AddDays(2);

        await using var seeded = await SeedAsync(
            Order(day, OrderStatus.Delivered, 40m, 6m),
            Order(day, OrderStatus.Delivered, 20m, 3m),
            Order(day, OrderStatus.Rejected, 0m, 0m, RejectionReason.TooBusy));

        // 60 over 2, not 60 over 3. Dividing by every order placed would report an average
        // nobody was charged, and would sink every time the kitchen had a bad week.
        (await ReportAsync(owner, day, day)).Totals.AverageOrderUsd.ShouldBe(30m);
    }

    [Fact]
    public async Task The_rejection_rate_does_not_blame_the_restaurant_for_a_cancellation()
    {
        var owner = await SignInAsync(Owner);
        var day = Window.AddDays(3);

        await using var seeded = await SeedAsync(
            Order(day, OrderStatus.Delivered, 10m, 1m),
            Order(day, OrderStatus.Delivered, 10m, 1m),
            Order(day, OrderStatus.Rejected, 10m, 1m, RejectionReason.TooBusy),
            Order(day, OrderStatus.Cancelled, 10m, 1m));

        // One refusal out of four orders. The cancellation is in the denominator because it did
        // arrive, and out of the numerator because the restaurant did not turn it away.
        (await ReportAsync(owner, day, day)).Totals.RejectionRate.ShouldBe(0.25m);
    }

    [Fact]
    public async Task The_reasons_are_broken_out_commonest_first()
    {
        var owner = await SignInAsync(Owner);
        var day = Window.AddDays(4);

        await using var seeded = await SeedAsync(
            Order(day, OrderStatus.Rejected, 10m, 1m, RejectionReason.OutOfStock),
            Order(day, OrderStatus.Rejected, 10m, 1m, RejectionReason.OutOfStock),
            Order(day, OrderStatus.Rejected, 10m, 1m, RejectionReason.TooBusy),
            Order(day, OrderStatus.Delivered, 10m, 1m));

        var reasons = (await ReportAsync(owner, day, day)).Rejections;

        reasons[0].Reason.ShouldBe(RejectionReason.OutOfStock);
        reasons[0].Count.ShouldBe(2);

        // Of the refusals, not of all four orders. "Two thirds of what you turn away is because
        // you have run out" is the sentence a restaurant can act on.
        reasons[0].Share.ShouldBe(0.6667m);
        reasons[1].Reason.ShouldBe(RejectionReason.TooBusy);
    }

    [Fact]
    public async Task A_day_with_nothing_on_it_still_appears()
    {
        var owner = await SignInAsync(Owner);
        var day = Window.AddDays(10);

        await using var seeded = await SeedAsync(
            Order(day, OrderStatus.Delivered, 10m, 1m),
            Order(day.AddDays(2), OrderStatus.Delivered, 10m, 1m));

        var report = await ReportAsync(owner, day, day.AddDays(2));

        // Three days, not two. A missing day draws as a gap, and a gap reads as missing data
        // rather than as a quiet Tuesday.
        report.Days.Count.ShouldBe(3);
        report.Days[1].Date.ShouldBe(day.AddDays(1));
        report.Days[1].Orders.ShouldBe(0);
        report.Days.Select(d => d.Date).ShouldBe(report.Days.Select(d => d.Date).Order().ToList());
    }

    [Fact]
    public async Task An_order_taken_after_midnight_is_reported_on_the_day_the_kitchen_worked()
    {
        var owner = await SignInAsync(Owner);
        var day = Window.AddDays(15);

        // 22:30 UTC is half past one the next morning in Beirut, which is +3 in summer. The
        // kitchen was working the evening of `day`; the UTC clock had already rolled over.
        //
        // This is the case the whole BusinessDate column exists for. Grouping on PlacedAt would
        // put this order on the following day - and only for half the year, since the offset
        // changes with the season, which is the kind of wrongness nobody can explain afterwards.
        await using var seeded = await SeedAsync(
            Order(day, OrderStatus.Delivered, 42m, 6m, placedAtUtc:
                new DateTimeOffset(day.ToDateTime(new TimeOnly(22, 30)), TimeSpan.Zero)));

        var report = await ReportAsync(owner, day, day.AddDays(1));

        report.Days[0].RevenueUsd.ShouldBe(42m, "the evening the kitchen actually worked");
        report.Days[1].RevenueUsd.ShouldBe(0m, "not the next morning, which is where UTC puts it");
    }

    [Fact]
    public async Task Orders_outside_the_range_are_not_counted()
    {
        var owner = await SignInAsync(Owner);
        var day = Window.AddDays(20);

        await using var seeded = await SeedAsync(
            Order(day.AddDays(-1), OrderStatus.Delivered, 99m, 9m),
            Order(day, OrderStatus.Delivered, 10m, 1m),
            Order(day.AddDays(1), OrderStatus.Delivered, 99m, 9m));

        // Both ends inclusive, and nothing either side of them.
        var report = await ReportAsync(owner, day, day);

        report.Totals.Orders.ShouldBe(1);
        report.Totals.RevenueUsd.ShouldBe(10m);
    }

    [Fact]
    public async Task Commission_is_what_each_order_was_charged_not_the_current_rate()
    {
        var owner = await SignInAsync(Owner);
        var admin = await SignInAsync("admin@ordering.test");
        var day = Window.AddDays(30);
        var restaurantId = await RestaurantIdAsync();
        var before = await CommissionAsync();

        await using var seeded = await SeedAsync(Order(day, OrderStatus.Delivered, 100m, 18m));

        try
        {
            (await admin.PutAsJsonAsync($"/api/platform/restaurants/{restaurantId}/commission",
                new { commissionPercent = before + 10m }, Ct)).EnsureSuccessStatusCode();

            // The report of a month already gone must not move because somebody renegotiated
            // today. Every order carries the rate it was charged at, and this reads that.
            (await ReportAsync(owner, day, day)).Totals.CommissionUsd.ShouldBe(18m);
        }
        finally
        {
            (await admin.PutAsJsonAsync($"/api/platform/restaurants/{restaurantId}/commission",
                new { commissionPercent = before }, Ct)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task One_restaurant_never_sees_anothers_numbers()
    {
        var shawarma = await SignInAsync(Owner);
        var friesLab = await SignInAsync("owner@frieslab.test");
        var day = Window.AddDays(40);

        await using var seeded = await SeedAsync(Order(day, OrderStatus.Delivered, 77m, 11m));

        (await ReportAsync(shawarma, day, day)).Totals.RevenueUsd.ShouldBe(77m);

        // Same days, a different restaurant, and none of it is theirs.
        (await ReportAsync(friesLab, day, day)).Totals.RevenueUsd.ShouldBe(0m);
    }

    // ------------------------------------------------------------------ who may look

    [Fact]
    public async Task A_staff_member_cannot_read_the_report()
    {
        var staff = await SignInAsync("staff@frieslab.test");

        // Revenue and commission are what the business earns and is charged.
        (await staff.GetAsync("/api/restaurant/reports/summary", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_customer_cannot_read_a_report()
    {
        var customer = await SignInAsync("rita@example.test");

        (await customer.GetAsync("/api/restaurant/reports/summary", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ the range itself

    [Fact]
    public async Task A_range_with_no_ends_covers_the_last_month_up_to_today()
    {
        var owner = await SignInAsync(Owner);

        var report = await ReportAsync(owner, null, null);

        // Today in the restaurant's timezone, not the caller's — the test clock pins the local
        // date, so this also pins that the server is the one deciding what "today" means.
        report.To.ShouldBe(factory.Clock.LocalToday);
        report.Days.Count.ShouldBe(30);
        report.From.ShouldBe(report.To.AddDays(-29));
    }

    [Fact]
    public async Task A_backwards_range_is_refused()
    {
        var owner = await SignInAsync(Owner);

        var response = await owner.GetAsync(
            "/api/restaurant/reports/summary?from=2026-03-10&to=2026-03-01", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_range_of_years_is_refused()
    {
        var owner = await SignInAsync(Owner);

        // Every day comes back as a row whether anything happened on it or not, so an unbounded
        // range is a response the size of the gap between the dates. A mistyped year is how a
        // request for three days becomes a request for a thousand.
        var response = await owner.GetAsync(
            "/api/restaurant/reports/summary?from=2020-01-01&to=2026-01-01", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------ history filtering

    [Fact]
    public async Task The_history_can_be_narrowed_to_a_day()
    {
        var owner = await SignInAsync(Owner);
        var day = Window.AddDays(50);

        await using var seeded = await SeedAsync(
            Order(day.AddDays(-1), OrderStatus.Delivered, 10m, 1m),
            Order(day, OrderStatus.Delivered, 10m, 1m),
            Order(day, OrderStatus.Delivered, 10m, 1m),
            Order(day.AddDays(1), OrderStatus.Delivered, 10m, 1m));

        var page = await owner.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>(
            $"/api/restaurant/orders?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}&pageSize=50", Ct);

        // The filter and the report have to agree about which day an order belongs to, which is
        // why both read the business date rather than the UTC timestamp.
        page!.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_history_filter_and_the_report_agree()
    {
        var owner = await SignInAsync(Owner);
        var day = Window.AddDays(60);

        await using var seeded = await SeedAsync(
            Order(day, OrderStatus.Delivered, 10m, 1m),
            Order(day, OrderStatus.Rejected, 10m, 1m, RejectionReason.TooBusy),
            Order(day.AddDays(3), OrderStatus.Delivered, 10m, 1m));

        var page = await owner.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>(
            $"/api/restaurant/orders?from={day:yyyy-MM-dd}&to={day.AddDays(3):yyyy-MM-dd}&pageSize=50", Ct);
        var report = await ReportAsync(owner, day, day.AddDays(3));

        // Two screens answering the same question have to give the same number, or somebody will
        // spend an afternoon working out which one is lying.
        page!.TotalCount.ShouldBe(report.Totals.Orders);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<RestaurantReportResponse> ReportAsync(
        HttpClient client, DateOnly? from, DateOnly? to)
    {
        var query = (from, to) switch
        {
            (null, null) => string.Empty,
            _ => $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}",
        };

        var response = await client.GetAsync($"/api/restaurant/reports/summary{query}", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"{(int)response.StatusCode}: {body}");

        return (await response.Content.ReadFromJsonAsync<RestaurantReportResponse>(Ct))!;
    }

    private sealed record Seed(
        DateOnly Date, OrderStatus Status, decimal TotalUsd, decimal CommissionUsd,
        RejectionReason? Reason, DateTimeOffset? PlacedAtUtc);

    /// <param name="placedAtUtc">
    /// Only where a test needs the UTC timestamp to disagree with the business date. Everything
    /// else gets a mid-evening time on the same day, where the two happen to agree.
    /// </param>
    private static Seed Order(
        DateOnly date, OrderStatus status, decimal totalUsd, decimal commissionUsd,
        RejectionReason? reason = null, DateTimeOffset? placedAtUtc = null) =>
        new(date, status, totalUsd, commissionUsd, reason, placedAtUtc);

    /// <summary>
    /// Writes orders on days that have already been and gone, and takes them away again.
    ///
    /// <para>
    /// Checkout can only produce today, so a week of history has to be written directly. The
    /// returned handle deletes exactly what it created — the restaurant is shared with the rest
    /// of the suite, and a report is a count of everything.
    /// </para>
    /// </summary>
    private async Task<SeededOrders> SeedAsync(params Seed[] seeds)
    {
        var restaurantId = await RestaurantIdAsync();

        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        var customerId = await db.Users.Where(u => u.Email == "rita@example.test")
            .Select(u => u.Id).FirstAsync(Ct);

        var ids = new List<Guid>();

        foreach (var seed in seeds)
        {
            var id = Guid.NewGuid();
            ids.Add(id);

            db.Orders.Add(new Domain.Orders.Order
            {
                Id = id,
                OrderNumber = $"RPT-{Guid.NewGuid():N}"[..16],
                CustomerId = customerId,
                RestaurantId = restaurantId,
                FulfillmentType = FulfillmentType.Pickup,
                Status = seed.Status,
                RejectionReason = seed.Reason,
                PaymentMethod = PaymentMethod.CashOnDelivery,
                PaymentStatus = PaymentStatus.Pending,
                SubtotalUsd = seed.TotalUsd,
                TotalUsd = seed.TotalUsd,
                ExchangeRateLbp = 89_500m,
                CommissionPercent = 18m,
                CommissionUsd = seed.CommissionUsd,
                PromisedMinutesMin = 20,
                PromisedMinutesMax = 30,
                IdempotencyKey = Guid.NewGuid(),
                PlacedAt = seed.PlacedAtUtc
                    ?? new DateTimeOffset(seed.Date.ToDateTime(new TimeOnly(19, 0)), TimeSpan.Zero),
                BusinessDate = seed.Date,
            });
        }

        await db.SaveChangesAsync(Ct);
        return new SeededOrders(factory, ids);
    }

    private sealed class SeededOrders(ApiFactory factory, List<Guid> ids) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
            await db.Orders.Where(o => ids.Contains(o.Id)).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    private async Task<Guid> RestaurantIdAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Restaurants.Where(r => r.Slug == Slug).Select(r => r.Id).FirstAsync(Ct);
    }

    private async Task<decimal> CommissionAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Restaurants.Where(r => r.Slug == Slug)
            .Select(r => r.CommissionPercent).FirstAsync(Ct);
    }

    private async Task<HttpClient> SignInAsync(string email)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, DatabaseSeeder.SeedPassword), Ct);
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync<AuthTokensResponse>(Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        return client;
    }
}
