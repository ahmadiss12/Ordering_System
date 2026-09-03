using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderingSystem.Api.IntegrationTests.Auth;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Features.Cart;
using OrderingSystem.Application.Features.Orders;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Infrastructure.Persistence.Seed;

namespace OrderingSystem.Api.IntegrationTests.Orders;

/// <summary>
/// Moving an order: accepting, refusing, cooking, handing over, and backing out.
/// <para>
/// The refusals are the point. A state machine that only ever lets the right move through is
/// untested — what proves it works is the delivered order that cannot be un-delivered, the pickup
/// order that never goes out for delivery, and the second tablet that does not get to accept an
/// order the first one already accepted.
/// </para>
/// </summary>
public sealed class OrderMovementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------ the happy path

    [Fact]
    public async Task A_pickup_order_walks_from_placed_to_delivered()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        (await MoveAsync(kitchen, placed.Id, OrderStatus.Accepted)).Status
            .ShouldBe(OrderStatus.Accepted);
        (await MoveAsync(kitchen, placed.Id, OrderStatus.Preparing)).Status
            .ShouldBe(OrderStatus.Preparing);
        (await MoveAsync(kitchen, placed.Id, OrderStatus.ReadyForPickup)).Status
            .ShouldBe(OrderStatus.ReadyForPickup);

        var delivered = await MoveAsync(kitchen, placed.Id, OrderStatus.Delivered);
        delivered.Status.ShouldBe(OrderStatus.Delivered);

        // Every step recorded, in order, with the account that made it — which is the whole
        // reason the trail exists rather than a status column on its own.
        delivered.Events.Select(e => e.ToStatus).ShouldBe([
            OrderStatus.Placed,
            OrderStatus.Accepted,
            OrderStatus.Preparing,
            OrderStatus.ReadyForPickup,
            OrderStatus.Delivered,
        ]);

        delivered.Events[0].FromStatus.ShouldBeNull("an order comes from nowhere");
        delivered.Events[0].ChangedBy.ShouldBe("Rita Customer");

        delivered.Events[1].FromStatus.ShouldBe(OrderStatus.Placed);
        delivered.Events.Skip(1).ShouldAllBe(e => e.ChangedBy == "Sami Staff");

        // Nothing left to press. A finished order is finished for both parties.
        delivered.AvailableTransitions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_delivery_order_goes_out_rather_than_to_the_counter()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceDeliveryOrderAsync(customer, "rita@example.test", "Cheese Lab Fries", 2);

        await MoveAsync(kitchen, placed.Id, OrderStatus.Accepted);
        var preparing = await MoveAsync(kitchen, placed.Id, OrderStatus.Preparing);

        // The fork. The same status, one step earlier, offers a different next move on each
        // fulfillment type, so the status always says something true about where the food is.
        preparing.AvailableTransitions.ShouldBe(
            [OrderStatus.OutForDelivery, OrderStatus.Cancelled], ignoreOrder: true);

        var out_ = await MoveAsync(kitchen, placed.Id, OrderStatus.OutForDelivery);
        out_.AvailableTransitions.ShouldBe([OrderStatus.Delivered]);

        (await MoveAsync(kitchen, placed.Id, OrderStatus.Delivered)).Status
            .ShouldBe(OrderStatus.Delivered);
    }

    [Fact]
    public async Task A_pickup_order_is_never_sent_out_for_delivery()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        await MoveAsync(kitchen, placed.Id, OrderStatus.Accepted);
        await MoveAsync(kitchen, placed.Id, OrderStatus.Preparing);

        var refused = await PostMoveAsync(kitchen, placed.Id, OrderStatus.OutForDelivery);

        refused.Status.ShouldBe(HttpStatusCode.Conflict);
        // Says which kind of order it is, rather than only that the move is not allowed.
        refused.Body.ShouldContain("pickup order");
    }

    // ------------------------------------------------------------------ the customer's side

    [Fact]
    public async Task A_customer_cancels_while_it_is_still_theirs_to_cancel()
    {
        var customer = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        var cancelled = await MoveAsync(customer, placed.Id, OrderStatus.Cancelled);

        cancelled.Status.ShouldBe(OrderStatus.Cancelled);
        // No form stood between them and the button, so nothing lands in the column the
        // rejection-rate report reads.
        cancelled.RejectionReason.ShouldBeNull();
        cancelled.Events[^1].ChangedBy.ShouldBe("Rita Customer");
    }

    [Fact]
    public async Task A_customer_can_still_cancel_after_the_kitchen_has_seen_it()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        await MoveAsync(kitchen, placed.Id, OrderStatus.Accepted);

        // Accepted means somebody saw it; Preparing means food is being made. The line is drawn
        // at the second, not the first.
        (await MoveAsync(customer, placed.Id, OrderStatus.Cancelled)).Status
            .ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task A_customer_cannot_cancel_once_cooking_has_started()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        await MoveAsync(kitchen, placed.Id, OrderStatus.Accepted);
        await MoveAsync(kitchen, placed.Id, OrderStatus.Preparing);

        var refused = await PostMoveAsync(customer, placed.Id, OrderStatus.Cancelled);

        // A conflict rather than a 403: they could have done this a minute ago, so what changed
        // is the state, and the message says what to do instead.
        refused.Status.ShouldBe(HttpStatusCode.Conflict);
        refused.Body.ShouldContain("Call the restaurant");
    }

    [Fact]
    public async Task A_customer_cannot_accept_their_own_order()
    {
        var customer = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        var refused = await PostMoveAsync(customer, placed.Id, OrderStatus.Accepted);

        refused.Status.ShouldBe(HttpStatusCode.Forbidden);
        refused.Body.ShouldContain("Only the restaurant");
    }

    [Fact]
    public async Task A_restaurant_cannot_cancel_on_the_customers_behalf()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        // Backing out before accepting is a rejection, and a rejection is what the report counts.
        // Letting a restaurant call it a cancellation instead would hide it.
        var refused = await PostMoveAsync(kitchen, placed.Id, OrderStatus.Cancelled);

        refused.Status.ShouldBe(HttpStatusCode.Forbidden);
        refused.Body.ShouldContain("Only the customer");
    }

    // ------------------------------------------------------------------ refusing, with a reason

    [Fact]
    public async Task Refusing_an_order_without_a_reason_is_refused_itself()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        var refused = await PostMoveAsync(kitchen, placed.Id, OrderStatus.Rejected);

        refused.Status.ShouldBe(HttpStatusCode.BadRequest);
        refused.Body.ShouldContain("reason");

        // And the order did not move.
        (await DetailAsync(kitchen, placed.Id)).Status.ShouldBe(OrderStatus.Placed);
    }

    [Fact]
    public async Task A_refused_order_records_why_and_who()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        var rejected = await MoveAsync(
            kitchen, placed.Id, OrderStatus.Rejected, RejectionReason.OutOfStock, "no potatoes left");

        rejected.Status.ShouldBe(OrderStatus.Rejected);
        rejected.RejectionReason.ShouldBe(RejectionReason.OutOfStock);
        rejected.RejectionNote.ShouldBe("no potatoes left");

        var last = rejected.Events[^1];
        last.ToStatus.ShouldBe(OrderStatus.Rejected);
        last.Note.ShouldBe("no potatoes left");
        last.ChangedBy.ShouldBe("Sami Staff");

        // The customer sees the same answer, which is the point of recording it on the order.
        var asCustomer = await DetailAsync(customer, placed.Id);
        asCustomer.RejectionReason.ShouldBe(RejectionReason.OutOfStock);
    }

    [Fact]
    public async Task A_restaurant_backing_out_of_an_accepted_order_needs_a_reason_too()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        await MoveAsync(kitchen, placed.Id, OrderStatus.Accepted);

        var bare = await PostMoveAsync(kitchen, placed.Id, OrderStatus.Cancelled);
        bare.Status.ShouldBe(HttpStatusCode.BadRequest);

        var cancelled = await MoveAsync(
            kitchen, placed.Id, OrderStatus.Cancelled, RejectionReason.TooBusy, "power cut");

        // Cancelled, not rejected — but it still carries a reason, so the report that asks
        // "which orders did this restaurant drop" finds it either way.
        cancelled.Status.ShouldBe(OrderStatus.Cancelled);
        cancelled.RejectionReason.ShouldBe(RejectionReason.TooBusy);
    }

    [Fact]
    public async Task A_reason_on_a_move_that_does_not_take_one_is_refused()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        // Accepting an order is not a refusal. A reason here would land in the column the
        // rejection-rate report reads and quietly make that report wrong, so it is refused
        // rather than dropped on the floor.
        var refused = await PostMoveAsync(
            kitchen, placed.Id, OrderStatus.Accepted, RejectionReason.TooBusy);

        refused.Status.ShouldBe(HttpStatusCode.BadRequest);
        (await DetailAsync(kitchen, placed.Id)).Status.ShouldBe(OrderStatus.Placed);
    }

    // ------------------------------------------------------------------ moves that go nowhere

    [Fact]
    public async Task A_finished_order_cannot_change_any_further()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        await MoveAsync(kitchen, placed.Id, OrderStatus.Accepted);
        await MoveAsync(kitchen, placed.Id, OrderStatus.Preparing);
        await MoveAsync(kitchen, placed.Id, OrderStatus.ReadyForPickup);
        await MoveAsync(kitchen, placed.Id, OrderStatus.Delivered);

        var backwards = await PostMoveAsync(kitchen, placed.Id, OrderStatus.Preparing);
        backwards.Status.ShouldBe(HttpStatusCode.Conflict);
        backwards.Body.ShouldContain("delivered");

        // Not even the customer, and not even to cancel.
        (await PostMoveAsync(customer, placed.Id, OrderStatus.Cancelled)).Status
            .ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Pressing_the_same_button_twice_says_so_rather_than_erroring()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        await MoveAsync(kitchen, placed.Id, OrderStatus.Accepted);
        var again = await PostMoveAsync(kitchen, placed.Id, OrderStatus.Accepted);

        again.Status.ShouldBe(HttpStatusCode.Conflict);
        again.Body.ShouldContain("already accepted");

        // And it left no second event behind. A trail with two accepts in it would make the
        // prep-time figures derived from it wrong.
        var order = await DetailAsync(kitchen, placed.Id);
        order.Events.Count(e => e.ToStatus == OrderStatus.Accepted).ShouldBe(1);
    }

    [Fact]
    public async Task An_order_cannot_skip_a_step()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        // Straight from placed to delivered would leave a kitchen screen showing food that was
        // never cooked, and a trail with a hole in it.
        var skipped = await PostMoveAsync(kitchen, placed.Id, OrderStatus.Delivered);

        skipped.Status.ShouldBe(HttpStatusCode.Conflict);
        skipped.Body.ShouldContain("cannot go from placed to delivered");
    }

    // ------------------------------------------------------------------ who may touch it

    [Fact]
    public async Task Another_restaurant_cannot_move_this_order()
    {
        var customer = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        var stranger = await SignInAsync("owner@mezze.test");
        var refused = await PostMoveAsync(stranger, placed.Id, OrderStatus.Accepted);

        // Not found rather than forbidden: the query filter hid it before anything else ran, and
        // a 403 would confirm the order exists.
        refused.Status.ShouldBe(HttpStatusCode.NotFound);
        (await DetailAsync(await SignInAsync("staff@frieslab.test"), placed.Id)).Status
            .ShouldBe(OrderStatus.Placed);
    }

    [Fact]
    public async Task Another_customer_cannot_move_this_order()
    {
        var rita = await SignInAsync("rita@example.test");
        var joe = await SignInAsync("joe@example.test");
        var placed = await PlaceOrderAsync(rita, "Cheese Lab Fries", 2);

        (await PostMoveAsync(joe, placed.Id, OrderStatus.Cancelled)).Status
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Moving_an_order_needs_somebody_signed_in()
    {
        var customer = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync(
            $"/api/orders/{placed.Id}/status",
            new ChangeOrderStatusRequest(OrderStatus.Cancelled, null, null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Somebody_who_works_there_can_still_cancel_their_own_lunch()
    {
        // A cook ordering from their own kitchen is staff and customer at once. Which hat they
        // are wearing is decided by the move, not by the claim on their token.
        var cook = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(cook, "Cheese Lab Fries", 2);

        var order = await DetailAsync(cook, placed.Id);
        order.AvailableTransitions.ShouldBe(
            [OrderStatus.Accepted, OrderStatus.Rejected, OrderStatus.Cancelled], ignoreOrder: true);

        // Cancelling is a customer's move and accepting is the restaurant's, and this one person
        // can make either.
        (await MoveAsync(cook, placed.Id, OrderStatus.Cancelled)).Status
            .ShouldBe(OrderStatus.Cancelled);
    }

    // ------------------------------------------------------------------ two tablets, one order

    [Fact]
    public async Task Two_tablets_pressing_accept_leave_exactly_one_accept()
    {
        var customer = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        var counter = await SignInAsync("staff@frieslab.test");
        var kitchen = await SignInAsync("owner@frieslab.test");

        var both = await Task.WhenAll(
            PostMoveAsync(counter, placed.Id, OrderStatus.Accepted),
            PostMoveAsync(kitchen, placed.Id, OrderStatus.Accepted));

        // Whether the loser is stopped by the rowversion or by reading the new status first is a
        // matter of timing, and either way it is a 409. What must never happen is two accepts.
        both.Count(r => r.Status == HttpStatusCode.OK).ShouldBe(1,
            $"got {string.Join(" and ", both.Select(r => (int)r.Status))}");
        both.Count(r => r.Status == HttpStatusCode.Conflict).ShouldBe(1);

        var order = await DetailAsync(counter, placed.Id);
        order.Status.ShouldBe(OrderStatus.Accepted);
        order.Events.Count(e => e.ToStatus == OrderStatus.Accepted).ShouldBe(1);
    }

    [Fact]
    public async Task The_rowversion_stops_a_write_that_never_saw_the_first_one()
    {
        var customer = await SignInAsync("rita@example.test");
        var placed = await PlaceOrderAsync(customer, "Cheese Lab Fries", 2);

        // Below the API on purpose. The test above proves the outcome a kitchen sees; this proves
        // the mechanism underneath it, which timing alone could otherwise let pass untested.
        await using var first = factory.CreateDbContext(TestTenant.PlatformAdmin());
        await using var second = factory.CreateDbContext(TestTenant.PlatformAdmin());

        var a = await first.Orders.FirstAsync(o => o.Id == placed.Id, Ct);
        var b = await second.Orders.FirstAsync(o => o.Id == placed.Id, Ct);

        a.Status = OrderStatus.Accepted;
        await first.SaveChangesAsync(Ct);

        b.Status = OrderStatus.Rejected;
        b.RejectionReason = RejectionReason.TooBusy;

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));
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

    /// <summary>Moves the order and insists it worked, handing back the whole order.</summary>
    private static async Task<OrderDetailResponse> MoveAsync(
        HttpClient client, Guid orderId, OrderStatus to,
        RejectionReason? reason = null, string? note = null)
    {
        var result = await PostMoveAsync(client, orderId, to, reason, note);
        result.Status.ShouldBe(HttpStatusCode.OK, $"moving to {to} failed: {result.Body}");

        return System.Text.Json.JsonSerializer.Deserialize<OrderDetailResponse>(result.Body, Json)!;
    }

    /// <summary>Posts and hands back both status and body, because the body is where the reason is.</summary>
    private static async Task<(HttpStatusCode Status, string Body)> PostMoveAsync(
        HttpClient client, Guid orderId, OrderStatus to,
        RejectionReason? reason = null, string? note = null)
    {
        var response = await client.PostAsJsonAsync($"/api/orders/{orderId}/status",
            new ChangeOrderStatusRequest(to, reason, note), Ct);

        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<OrderDetailResponse> DetailAsync(HttpClient client, Guid orderId)
    {
        var response = await client.GetAsync($"/api/orders/{orderId}", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"reading failed with {(int)response.StatusCode}: {body}");

        return System.Text.Json.JsonSerializer.Deserialize<OrderDetailResponse>(body, Json)!;
    }

    /// <summary>Matches how the API serialises, and cached because the analyzer is right.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions Json =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private async Task<PlacedOrderResponse> PlaceOrderAsync(
        HttpClient client, string itemName, int quantity)
    {
        var restaurantId = await RestaurantIdAsync();
        await StockBasketAsync(client, restaurantId, itemName, quantity);

        var quote = await QuoteAsync(client, restaurantId, "Pickup");

        return await CheckoutAsync(client, restaurantId, new CheckoutRequest(
            FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null,
            quote.TotalUsd, Guid.NewGuid()));
    }

    private async Task<PlacedOrderResponse> PlaceDeliveryOrderAsync(
        HttpClient client, string email, string itemName, int quantity)
    {
        var restaurantId = await RestaurantIdAsync();
        await StockBasketAsync(client, restaurantId, itemName, quantity);

        var addressId = await ServedAddressIdAsync(email, restaurantId);
        var quote = await QuoteAsync(client, restaurantId, "Delivery", addressId);

        return await CheckoutAsync(client, restaurantId, new CheckoutRequest(
            FulfillmentType.Delivery, addressId, PaymentMethod.CashOnDelivery, null,
            quote.TotalUsd, Guid.NewGuid()));
    }

    private async Task StockBasketAsync(HttpClient client, Guid restaurantId, string itemName, int quantity)
    {
        (await client.DeleteAsync($"/api/restaurants/{restaurantId}/cart", Ct)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/cart/lines",
            new AddCartLineRequest(await ItemIdAsync(itemName), quantity, null, []), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"adding failed with {(int)response.StatusCode}: {body}");
    }

    private static async Task<QuoteResponse> QuoteAsync(
        HttpClient client, Guid restaurantId, string fulfillment, Guid? addressId = null)
    {
        var url = $"/api/restaurants/{restaurantId}/cart/quote?fulfillment={fulfillment}"
            + (addressId is null ? string.Empty : $"&addressId={addressId}");

        var response = await client.GetAsync(url, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"the quote failed with {(int)response.StatusCode}: {body}");

        return System.Text.Json.JsonSerializer.Deserialize<QuoteResponse>(body, Json)!;
    }

    private static async Task<PlacedOrderResponse> CheckoutAsync(
        HttpClient client, Guid restaurantId, CheckoutRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/orders", request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        ((int)response.StatusCode).ShouldBe(201, $"checkout failed: {body}");

        return System.Text.Json.JsonSerializer.Deserialize<PlacedOrderResponse>(body, Json)!;
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

    private async Task<Guid> ServedAddressIdAsync(string email, Guid restaurantId)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        return await (
            from address in db.Addresses
            join zone in db.RestaurantZones on address.ZoneId equals zone.ZoneId
            where address.User.Email == email && zone.RestaurantId == restaurantId && zone.IsActive
            select address.Id).FirstAsync(Ct);
    }
}
