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
    public async Task Staff_see_their_restaurants_orders_and_nobody_elses()
    {
        await using var db = scenario.Context(TestTenant.Staff(scenario.StaffA, scenario.RestaurantA));

        var visible = await db.Orders
            .Select(o => new { o.Id, o.RestaurantId, o.CustomerId })
            .ToListAsync(Ct);

        visible.ShouldNotBeEmpty("staff must still see their own restaurant's orders");

        // Two ways in, and only two: the order belongs to the restaurant they work for, or it
        // belongs to them. Restaurant B has an order of each kind, so this would catch either
        // half being wrong.
        visible.ShouldAllBe(o =>
            o.RestaurantId == scenario.RestaurantA || o.CustomerId == scenario.StaffA);
        visible.ShouldContain(o => o.Id == scenario.OrderA);
        visible.ShouldNotContain(o => o.Id == scenario.OrderB);
    }

    [Fact]
    public async Task A_staff_member_still_sees_the_orders_they_placed_themselves()
    {
        await using var db = scenario.Context(TestTenant.Staff(scenario.StaffA, scenario.RestaurantA));

        // The bug this pins: a staff member's own order history used to disappear the moment they
        // were given a restaurant claim, because the filter read "a customer with no restaurant
        // sees their own". Being hired is not a reason to lose the dinner you ordered last week.
        var mine = await db.Orders.Where(o => o.CustomerId == scenario.StaffA).ToListAsync(Ct);
        var lines = await db.OrderLines
            .Where(l => l.OrderId == scenario.StaffAsCustomerOrder)
            .ToListAsync(Ct);

        mine.Select(o => o.Id).ShouldContain(scenario.StaffAsCustomerOrder);
        lines.ShouldNotBeEmpty("the order is useless without the items on it");
    }

    [Fact]
    public async Task Seeing_your_own_order_at_another_restaurant_is_not_a_way_into_that_restaurant()
    {
        await using var db = scenario.Context(TestTenant.Staff(scenario.StaffA, scenario.RestaurantA));

        // StaffA has an order at restaurant B, so restaurant B's rows are reachable through *that*
        // order and no other. The worry is a filter written as "or the order is at a restaurant I
        // have ordered from", which would hand over the whole tenant.
        var atB = await db.Orders.CountAsync(o => o.RestaurantId == scenario.RestaurantB, Ct);

        atB.ShouldBe(1);
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
    public void Only_a_platform_admin_passes_the_platform_check()
    {
        // A separate check from EnsureCanActFor, and this is why: an owner passes that one for
        // their own restaurant, which is right for a menu and wrong for the commission rate the
        // platform charges them.
        Should.Throw<ForbiddenException>(
            () => GuardFor(TestTenant.Staff(scenario.StaffA, scenario.RestaurantA)).RequirePlatformAdmin());
        Should.Throw<ForbiddenException>(
            () => GuardFor(TestTenant.Customer(scenario.CustomerA)).RequirePlatformAdmin());
        Should.Throw<ForbiddenException>(
            () => GuardFor(TestTenant.Anonymous).RequirePlatformAdmin());

        Should.NotThrow(() => GuardFor(TestTenant.PlatformAdmin()).RequirePlatformAdmin());
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
