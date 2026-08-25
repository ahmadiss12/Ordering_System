using Microsoft.EntityFrameworkCore;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Application.Common;
using OrderingSystem.Domain.Exceptions;

namespace OrderingSystem.Api.IntegrationTests.Tenancy;

/// <summary>
/// The single most important security property in the system: restaurant A cannot reach
/// restaurant B's data. Asserted against a real SQL Server, because these filters become SQL and
/// only SQL can prove what SQL returns.
/// </summary>
public sealed class TenantIsolationTests(TwoRestaurantScenario scenario)
    : IClassFixture<TwoRestaurantScenario>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Staff_see_only_their_own_restaurants_orders()
    {
        await using var db = scenario.Context(TestTenant.Staff(scenario.StaffA, scenario.RestaurantA));

        var visible = await db.Orders.Select(o => o.RestaurantId).ToListAsync(Ct);

        visible.ShouldAllBe(id => id == scenario.RestaurantA);
        visible.ShouldNotBeEmpty("staff must still see their own orders");
    }

    [Fact]
    public async Task Fetching_another_restaurants_order_by_id_finds_nothing()
    {
        await using var db = scenario.Context(TestTenant.Staff(scenario.StaffA, scenario.RestaurantA));

        // Knowing the id is not access. This is the direct-API-call case from spec §4 - the
        // endpoint turns this null into a 403.
        var stolen = await db.Orders.FirstOrDefaultAsync(o => o.Id == scenario.OrderB, Ct);

        stolen.ShouldBeNull();
    }

    [Fact]
    public async Task Order_lines_are_filtered_too_not_just_orders()
    {
        await using var db = scenario.Context(TestTenant.Staff(scenario.StaffA, scenario.RestaurantA));

        // The hole this closes: Orders was filtered and OrderLines was not, so querying the child
        // table directly returned every restaurant's item names and prices.
        var lines = await db.OrderLines.Select(l => l.ItemNameSnapshot).ToListAsync(Ct);

        lines.ShouldNotBeEmpty();
        lines.ShouldNotContain(name => name.Contains("B-0001", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Payments_and_events_are_filtered_too()
    {
        await using var db = scenario.Context(TestTenant.Staff(scenario.StaffA, scenario.RestaurantA));

        var payments = await db.Payments.CountAsync(Ct);
        var events = await db.OrderEvents.CountAsync(Ct);
        var foreignPayments = await db.Payments.CountAsync(p => p.OrderId == scenario.OrderB, Ct);
        var foreignEvents = await db.OrderEvents.CountAsync(e => e.OrderId == scenario.OrderB, Ct);

        payments.ShouldBeGreaterThan(0);
        events.ShouldBeGreaterThan(0);
        foreignPayments.ShouldBe(0);
        foreignEvents.ShouldBe(0);
    }

    [Fact]
    public async Task A_customer_sees_their_own_orders_and_no_others()
    {
        await using var db = scenario.Context(TestTenant.Customer(scenario.CustomerA));

        var visible = await db.Orders.Select(o => o.CustomerId).ToListAsync(Ct);

        visible.ShouldAllBe(id => id == scenario.CustomerA);
        visible.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_platform_admin_sees_across_restaurants()
    {
        await using var db = scenario.Context(TestTenant.PlatformAdmin());

        var restaurants = await db.Orders.Select(o => o.RestaurantId).Distinct().ToListAsync(Ct);

        restaurants.ShouldContain(scenario.RestaurantA);
        restaurants.ShouldContain(scenario.RestaurantB);
    }

    [Fact]
    public async Task An_anonymous_caller_sees_no_orders_at_all()
    {
        await using var db = scenario.Context(TestTenant.Anonymous);

        // Anonymous has a null UserId, which matches no CustomerId. Failing closed is the
        // behaviour we want from a missing identity, and it is worth pinning.
        (await db.Orders.CountAsync(Ct)).ShouldBe(0);
        (await db.OrderLines.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public void The_guard_refuses_a_write_aimed_at_another_restaurant()
    {
        var guard = GuardFor(TestTenant.Staff(scenario.StaffA, scenario.RestaurantA));

        Should.NotThrow(() => guard.EnsureCanActFor(scenario.RestaurantA));

        // Filters are a WHERE clause and an INSERT has no WHERE. Only this check stops a write
        // being stamped with someone else's restaurant id.
        Should.Throw<ForbiddenException>(() => guard.EnsureCanActFor(scenario.RestaurantB));
    }

    [Fact]
    public void The_guard_refuses_a_customer_acting_as_a_restaurant()
    {
        var guard = GuardFor(TestTenant.Customer(scenario.CustomerA));

        Should.Throw<ForbiddenException>(() => guard.EnsureCanActFor(scenario.RestaurantA));
        Should.Throw<ForbiddenException>(() => guard.RequireRestaurantId());
    }

    [Fact]
    public void A_platform_admin_may_act_for_any_restaurant()
    {
        var guard = GuardFor(TestTenant.PlatformAdmin());

        Should.NotThrow(() => guard.EnsureCanActFor(scenario.RestaurantA));
        Should.NotThrow(() => guard.EnsureCanActFor(scenario.RestaurantB));
    }

    private static TenantGuard GuardFor(ITenantContext tenant) => new(tenant);
}
