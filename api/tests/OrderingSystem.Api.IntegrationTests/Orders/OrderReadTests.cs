using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderingSystem.Api.IntegrationTests.Auth;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Common;
using OrderingSystem.Application.Features.Cart;
using OrderingSystem.Application.Features.Orders;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Infrastructure.Persistence.Seed;

namespace OrderingSystem.Api.IntegrationTests.Orders;

/// <summary>
/// Reading orders: a customer's history, a kitchen's queue, and one order in full.
/// <para>
/// Who can see what is the whole subject here. An order is visible to the person who placed it
/// and to the restaurant cooking it, and to nobody else — including another restaurant, which is
/// the property the spec calls the most important in the system.
/// </para>
/// </summary>
public sealed class OrderReadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_customer_sees_the_order_they_just_placed()
    {
        var client = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(client, "frieslab", "Cheese Lab Fries", 2);

        var mine = await client.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>(
            "/api/orders", Ct);

        mine!.Items.ShouldContain(o => o.Id == placed.Id);

        var row = mine.Items.Single(o => o.Id == placed.Id);
        row.OrderNumber.ShouldBe(placed.OrderNumber);
        row.Status.ShouldBe(OrderStatus.Placed);
        row.RestaurantName.ShouldBe("FriesLab");
        row.ItemCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_customer_never_sees_somebody_elses_order()
    {
        var rita = await SignInAsync("rita@example.test");
        var joe = await SignInAsync("joe@example.test");

        var ritas = await PlaceOrderAsync(rita, "frieslab", "Cheese Lab Fries", 2);

        var joes = await joe.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>("/api/orders", Ct);
        joes!.Items.ShouldNotContain(o => o.Id == ritas.Id);

        // Not found rather than forbidden: telling Joe it exists would confirm an order number
        // he guessed is real.
        var direct = await joe.GetAsync($"/api/orders/{ritas.Id}", Ct);
        direct.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_history_is_newest_first()
    {
        var client = await SignInAsync("joe@example.test");

        var first = await PlaceOrderAsync(client, "frieslab", "Cheese Lab Fries", 2);
        var second = await PlaceOrderAsync(client, "frieslab", "Buffalo Wings", 2);

        var mine = await client.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>("/api/orders", Ct);

        // A customer looks for what they ordered last night, not for their first ever order.
        var ids = mine!.Items.Select(o => o.Id).ToList();
        ids.IndexOf(second.Id).ShouldBeLessThan(ids.IndexOf(first.Id));
    }

    // ------------------------------------------------------------------ the detail

    [Fact]
    public async Task One_order_reads_back_with_its_own_copy_of_everything()
    {
        var client = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(client, "frieslab", "Truffle Parmesan Fries", 2);

        var order = await client.GetFromJsonAsync<OrderDetailResponse>($"/api/orders/{placed.Id}", Ct);

        order!.OrderNumber.ShouldBe(placed.OrderNumber);
        order.TotalUsd.ShouldBe(placed.TotalUsd);
        order.RestaurantPhone.ShouldNotBeNullOrWhiteSpace("a customer needs to be able to call them");

        var line = order.Lines.ShouldHaveSingleItem();
        line.Name.ShouldBe("Truffle Parmesan Fries");
        line.LineTotalUsd.ShouldBe(line.UnitPriceUsd * line.Quantity);

        // The lines still add up to what was charged.
        order.SubtotalUsd.ShouldBe(order.Lines.Sum(l => l.LineTotalUsd));
    }

    [Fact]
    public async Task The_detail_carries_the_trail_of_what_happened()
    {
        var client = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(client, "frieslab", "Cheese Lab Fries", 2);

        var order = await client.GetFromJsonAsync<OrderDetailResponse>($"/api/orders/{placed.Id}", Ct);

        var first = order!.Events.ShouldHaveSingleItem();
        first.FromStatus.ShouldBeNull("an order comes from nowhere");
        first.ToStatus.ShouldBe(OrderStatus.Placed);
        first.ChangedBy.ShouldBe("Rita Customer");
    }

    [Fact]
    public async Task A_customer_is_offered_only_cancelling()
    {
        var client = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(client, "frieslab", "Cheese Lab Fries", 2);

        var order = await client.GetFromJsonAsync<OrderDetailResponse>($"/api/orders/{placed.Id}", Ct);

        // The screen draws its buttons from this, so a button that would be refused is never
        // rendered in the first place.
        order!.AvailableTransitions.ShouldBe([OrderStatus.Cancelled]);
    }

    [Fact]
    public async Task The_kitchen_is_offered_accepting_and_refusing()
    {
        var customer = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(customer, "frieslab", "Cheese Lab Fries", 2);

        var staff = await SignInAsync("staff@frieslab.test");
        var order = await staff.GetFromJsonAsync<OrderDetailResponse>($"/api/orders/{placed.Id}", Ct);

        // The same order, a different party, a different set of buttons.
        order!.AvailableTransitions.ShouldBe(
            [OrderStatus.Accepted, OrderStatus.Rejected], ignoreOrder: true);
    }

    [Fact]
    public async Task A_pickup_order_carries_no_address()
    {
        var client = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(client, "frieslab", "Cheese Lab Fries", 2);

        var order = await client.GetFromJsonAsync<OrderDetailResponse>($"/api/orders/{placed.Id}", Ct);

        order!.DeliveryAddress.ShouldBeNull();
        order.DeliveryFeeUsd.ShouldBe(0m);
    }

    // ------------------------------------------------------------------ the kitchen's queue

    [Fact]
    public async Task Staff_see_their_restaurants_orders()
    {
        var customer = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(customer, "frieslab", "Cheese Lab Fries", 2);

        var staff = await SignInAsync("staff@frieslab.test");
        var queue = await staff.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>(
            "/api/restaurant/orders", Ct);

        queue!.Items.ShouldContain(o => o.Id == placed.Id);

        var row = queue.Items.Single(o => o.Id == placed.Id);
        row.CustomerName.ShouldBe("Rita Customer", "a kitchen needs to know whose order it is");
    }

    [Fact]
    public async Task The_queue_can_be_narrowed_to_the_statuses_a_screen_cares_about()
    {
        var customer = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(customer, "frieslab", "Cheese Lab Fries", 2);

        var staff = await SignInAsync("staff@frieslab.test");

        var live = await staff.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>(
            "/api/restaurant/orders?status=Placed", Ct);
        live!.Items.ShouldContain(o => o.Id == placed.Id);

        var finished = await staff.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>(
            "/api/restaurant/orders?status=Delivered", Ct);
        finished!.Items.ShouldNotContain(o => o.Id == placed.Id);
    }

    [Fact]
    public async Task The_queue_is_oldest_first_because_that_is_the_one_that_has_waited()
    {
        var customer = await SignInAsync("rita@example.test");

        var first = await PlaceOrderAsync(customer, "frieslab", "Cheese Lab Fries", 2);
        var second = await PlaceOrderAsync(customer, "frieslab", "Buffalo Wings", 2);

        var staff = await SignInAsync("staff@frieslab.test");
        var queue = await staff.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>(
            "/api/restaurant/orders?status=Placed", Ct);

        // The opposite of the history, and deliberately so. A kitchen works the order that has
        // been waiting longest; newest-first paging would put the ones most in need of attention
        // on the last page.
        var ids = queue!.Items.Select(o => o.Id).ToList();
        ids.IndexOf(first.Id).ShouldBeLessThan(ids.IndexOf(second.Id));
    }

    [Fact]
    public async Task A_queue_row_carries_the_promise_it_has_to_keep()
    {
        var customer = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(customer, "frieslab", "Cheese Lab Fries", 2);

        var staff = await SignInAsync("staff@frieslab.test");
        var queue = await staff.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>(
            "/api/restaurant/orders", Ct);

        // On the row rather than fetched per order: a kitchen screen refreshes every few seconds,
        // and asking each order for its detail to find out whether it is late would be one
        // request per row per refresh.
        var row = queue!.Items.Single(o => o.Id == placed.Id);
        row.PromisedMinutesMin.ShouldBe(placed.PromisedMinutesMin);
        row.PromisedMinutesMax.ShouldBe(placed.PromisedMinutesMax);
        row.PromisedMinutesMax.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_queue_row_carries_the_buttons_the_kitchen_may_press()
    {
        var customer = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(customer, "frieslab", "Cheese Lab Fries", 2);

        var staff = await SignInAsync("staff@frieslab.test");
        var queue = await staff.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>(
            "/api/restaurant/orders?status=Placed", Ct);

        // On the row, so a board of thirty cards draws thirty correct buttons without thirty
        // requests. It is a lookup in a frozen table, so it costs the database nothing.
        var row = queue!.Items.Single(o => o.Id == placed.Id);
        row.AvailableTransitions.ShouldBe(
            [OrderStatus.Accepted, OrderStatus.Rejected], ignoreOrder: true);
    }

    [Fact]
    public async Task A_history_row_offers_the_customers_own_moves_not_the_kitchens()
    {
        var customer = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(customer, "frieslab", "Cheese Lab Fries", 2);

        var mine = await customer.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>(
            "/api/orders", Ct);

        // The same order, a different list, a different set of buttons. A history that offered
        // Accept would draw a button the API refuses.
        var row = mine!.Items.Single(o => o.Id == placed.Id);
        row.AvailableTransitions.ShouldBe([OrderStatus.Cancelled]);
    }

    [Fact]
    public async Task One_restaurant_never_sees_anothers_orders()
    {
        var customer = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(customer, "frieslab", "Cheese Lab Fries", 2);

        // The property the spec calls the most important in the system.
        var stranger = await SignInAsync("owner@mezze.test");

        var queue = await stranger.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>(
            "/api/restaurant/orders", Ct);
        queue!.Items.ShouldNotContain(o => o.Id == placed.Id);

        var direct = await stranger.GetAsync($"/api/orders/{placed.Id}", Ct);
        direct.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_customer_cannot_open_a_restaurants_queue()
    {
        var client = await SignInAsync("rita@example.test");

        var response = await client.GetAsync("/api/restaurant/orders", Ct);

        // No restaurant_id claim, so the policy refuses before the query is ever built.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reading_orders_needs_somebody_to_be_signed_in()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/orders", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/restaurant/orders", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
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

    /// <summary>Places a pickup order and hands back what the API said about it.</summary>
    private async Task<PlacedOrderResponse> PlaceOrderAsync(
        HttpClient client, string slug, string itemName, int quantity)
    {
        var restaurantId = await RestaurantIdAsync(slug);
        var itemId = await ItemIdAsync(slug, itemName);

        (await client.DeleteAsync($"/api/restaurants/{restaurantId}/cart", Ct)).EnsureSuccessStatusCode();

        var added = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/cart/lines",
            new AddCartLineRequest(itemId, quantity, null, []), Ct);
        added.IsSuccessStatusCode.ShouldBeTrue(await added.Content.ReadAsStringAsync(Ct));

        var quote = await client.GetFromJsonAsync<QuoteResponse>(
            $"/api/restaurants/{restaurantId}/cart/quote?fulfillment=Pickup", Ct);

        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/orders",
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null,
                quote!.TotalUsd, Guid.NewGuid()), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"checkout failed with {(int)response.StatusCode}: {body}");

        return (await response.Content.ReadFromJsonAsync<PlacedOrderResponse>(Ct))!;
    }

    private async Task<Guid> RestaurantIdAsync(string slug)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Restaurants.Where(r => r.Slug == slug).Select(r => r.Id).FirstAsync(Ct);
    }

    private async Task<Guid> ItemIdAsync(string slug, string name)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.MenuItems
            .Where(i => i.Restaurant.Slug == slug && i.Name == name)
            .Select(i => i.Id).FirstAsync(Ct);
    }
}
