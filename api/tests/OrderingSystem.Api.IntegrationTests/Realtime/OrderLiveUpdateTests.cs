using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using OrderingSystem.Api.IntegrationTests.Auth;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Application.Features.Cart;
using OrderingSystem.Application.Features.Orders;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Infrastructure.Persistence.Seed;

namespace OrderingSystem.Api.IntegrationTests.Realtime;

/// <summary>
/// The live channel: who hears about an order, and — the part that matters — who does not.
///
/// <para>
/// A hub is one string away from being the widest hole in a multi-tenant system, because a group
/// name is just a string and nothing in SignalR knows what it means. These tests connect a real
/// client to the real hub over the real pipeline, so the group rules are proved rather than
/// asserted about a class in isolation.
/// </para>
/// </summary>
public sealed class OrderLiveUpdateTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>How long a message is given to arrive before the test calls it lost.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a connection that should hear nothing is watched after one that should hear
    /// something already has. Short on purpose: the positive delivery is the synchronisation
    /// point, so this is a margin rather than a wait.
    /// </summary>
    private static readonly TimeSpan Margin = TimeSpan.FromMilliseconds(500);

    // ------------------------------------------------------------------ who hears

    [Fact]
    public async Task The_kitchen_hears_about_an_order_the_moment_it_is_placed()
    {
        var customer = await SignInAsync("rita@example.test");

        await using var kitchen = await ListenAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer);

        var heard = await kitchen.NextAsync();

        heard.OrderId.ShouldBe(placed.Id);
        heard.OrderNumber.ShouldBe(placed.OrderNumber);
        heard.Status.ShouldBe(OrderStatus.Placed);
        heard.PreviousStatus.ShouldBeNull("a placed order came from nowhere");
    }

    [Fact]
    public async Task The_customer_hears_when_the_kitchen_accepts()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer);

        await using var tracker = await ListenAsync("rita@example.test");
        await AcceptAsync(kitchen, placed.Id);

        var heard = await tracker.NextAsync();

        heard.OrderId.ShouldBe(placed.Id);
        heard.Status.ShouldBe(OrderStatus.Accepted);
        heard.PreviousStatus.ShouldBe(OrderStatus.Placed, "a screen wants to know what changed");
    }

    [Fact]
    public async Task Another_restaurant_hears_nothing()
    {
        var customer = await SignInAsync("rita@example.test");

        await using var kitchen = await ListenAsync("staff@frieslab.test");
        await using var stranger = await ListenAsync("owner@mezze.test");

        await PlaceOrderAsync(customer);

        // The property the spec calls the most important in the system, on the one channel where
        // getting it wrong is invisible: nothing errors, another restaurant simply starts
        // hearing orders that are not theirs.
        await kitchen.NextAsync();
        await Task.Delay(Margin, Ct);

        stranger.Heard.ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_customer_hears_nothing()
    {
        var rita = await SignInAsync("rita@example.test");

        await using var kitchen = await ListenAsync("staff@frieslab.test");
        await using var joe = await ListenAsync("joe@example.test");

        await PlaceOrderAsync(rita);

        await kitchen.NextAsync();
        await Task.Delay(Margin, Ct);

        joe.Heard.ShouldBeEmpty();
    }

    [Fact]
    public async Task Somebody_who_is_both_staff_and_customer_hears_it_once()
    {
        // A cook ordering their own lunch. This failed when written: SignalR's Clients.Groups
        // walks each group without tracking connections it has already reached, so a connection
        // in both had the order pushed to it twice. The hub now puts every connection in exactly
        // one group — the same either/or the query filter on Order makes — and this is what
        // stops that regressing.
        var cook = await SignInAsync("staff@frieslab.test");

        await using var listener = await ListenAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(cook);

        await listener.NextAsync();
        await Task.Delay(Margin, Ct);

        listener.Heard.Count.ShouldBe(1);
        listener.Heard[0].OrderId.ShouldBe(placed.Id);
    }

    [Fact]
    public async Task A_refused_move_says_nothing_to_anybody()
    {
        var customer = await SignInAsync("rita@example.test");
        var kitchen = await SignInAsync("staff@frieslab.test");
        var placed = await PlaceOrderAsync(customer);

        await using var listener = await ListenAsync("staff@frieslab.test");

        // Straight to delivered, which the state machine refuses.
        var refused = await kitchen.PostAsJsonAsync($"/api/orders/{placed.Id}/status",
            new ChangeOrderStatusRequest(OrderStatus.Delivered, null, null), Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Then a real move, as the synchronisation point: once this arrives, anything the
        // refusal might have sent would already have arrived too.
        await AcceptAsync(kitchen, placed.Id);
        var heard = await listener.NextAsync();

        heard.Status.ShouldBe(OrderStatus.Accepted);
        listener.Heard.ShouldHaveSingleItem();
    }

    // ------------------------------------------------------------------ the wire

    [Fact]
    public async Task The_message_reaches_the_wire_with_the_names_a_browser_expects()
    {
        // The one contract in this system that no generated client checks. SignalR is not in the
        // OpenAPI document, so the TypeScript handler is written by hand — and a casing mismatch
        // does not fail anything, it just hands the screen undefined.
        var customer = await SignInAsync("rita@example.test");

        await using var kitchen = await ListenRawAsync("staff@frieslab.test");
        await PlaceOrderAsync(customer);

        var payload = await kitchen.NextAsync();

        foreach (var name in new[] { "orderId", "orderNumber", "status", "previousStatus", "at" })
        {
            payload.TryGetProperty(name, out _).ShouldBeTrue(
                $"the browser reads {name}; the payload has: "
                + string.Join(", ", payload.EnumerateObject().Select(p => p.Name)));
        }

        // A number, not a name. The generated TypeScript enum is numeric, and the two have to
        // agree or a kitchen screen matches on a status that never arrives.
        payload.GetProperty("status").GetInt32().ShouldBe((int)OrderStatus.Placed);
    }

    // ------------------------------------------------------------------ getting in

    [Fact]
    public async Task The_hub_turns_away_a_connection_with_no_token()
    {
        var response = await factory.CreateClient()
            .PostAsync("/hubs/orders/negotiate?negotiateVersion=1", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_browser_can_authenticate_with_the_token_in_the_query_string()
    {
        // A browser cannot set an Authorization header on a WebSocket, so this is the only way
        // a real kitchen screen ever connects.
        var token = await TokenAsync("staff@frieslab.test");

        var response = await factory.CreateClient().PostAsync(
            $"/hubs/orders/negotiate?negotiateVersion=1&access_token={token}", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_query_string_token_is_not_accepted_anywhere_else()
    {
        // Narrowed to the hub path on purpose. A bearer token in a query string ends up in proxy
        // logs, browser history and referrer headers, so it is tolerated exactly where the
        // browser leaves no choice and nowhere else.
        var token = await TokenAsync("staff@frieslab.test");

        var response = await factory.CreateClient()
            .GetAsync($"/api/restaurant/orders?access_token={token}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A connected client that keeps what it heard.</summary>
    private sealed class Listener<T>(HubConnection connection) : IAsyncDisposable
    {
        private readonly List<T> _heard = [];
        private readonly SemaphoreSlim _arrived = new(0);

        public IReadOnlyList<T> Heard
        {
            get { lock (_heard) { return _heard.ToArray(); } }
        }

        public void Record(T message)
        {
            lock (_heard) { _heard.Add(message); }
            _arrived.Release();
        }

        /// <summary>The next message, or a failed test if none arrives.</summary>
        public async Task<T> NextAsync()
        {
            (await _arrived.WaitAsync(Patience, Ct))
                .ShouldBeTrue($"nothing arrived on the hub within {Patience.TotalSeconds:0}s");

            lock (_heard) { return _heard[^1]; }
        }

        public async ValueTask DisposeAsync()
        {
            await connection.DisposeAsync();
            _arrived.Dispose();
        }
    }

    private async Task<Listener<OrderChanged>> ListenAsync(string email)
    {
        var connection = await ConnectAsync(email);
        var listener = new Listener<OrderChanged>(connection);
        connection.On<OrderChanged>("orderChanged", listener.Record);

        await connection.StartAsync(Ct);
        return listener;
    }

    private async Task<Listener<JsonElement>> ListenRawAsync(string email)
    {
        var connection = await ConnectAsync(email);
        var listener = new Listener<JsonElement>(connection);
        connection.On<JsonElement>("orderChanged", listener.Record);

        await connection.StartAsync(Ct);
        return listener;
    }

    private async Task<HubConnection> ConnectAsync(string email)
    {
        var token = await TokenAsync(email);

        return new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/orders"), options =>
            {
                // The in-process test server, so the hub runs in the same pipeline every other
                // test drives.
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);

                // Long polling, pinned rather than negotiated. WebSockets against a TestServer
                // need a factory of their own, and the transport is ASP.NET's code, not ours —
                // what these tests are about is the hub, the groups and the claims, and long
                // polling exercises all three identically.
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    private async Task<string> TokenAsync(string email)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new OrderingSystem.Application.Features.Auth.LoginRequest(email, DatabaseSeeder.SeedPassword), Ct);
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content
            .ReadFromJsonAsync<OrderingSystem.Application.Features.Auth.AuthTokensResponse>(Ct);

        return tokens!.AccessToken;
    }

    private async Task<HttpClient> SignInAsync(string email)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await TokenAsync(email));

        return client;
    }

    private static async Task AcceptAsync(HttpClient client, Guid orderId)
    {
        var response = await client.PostAsJsonAsync($"/api/orders/{orderId}/status",
            new ChangeOrderStatusRequest(OrderStatus.Accepted, null, null), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"accepting failed with {(int)response.StatusCode}: {body}");
    }

    private async Task<PlacedOrderResponse> PlaceOrderAsync(HttpClient client)
    {
        var restaurantId = await RestaurantIdAsync();
        var itemId = await ItemIdAsync("Cheese Lab Fries");

        (await client.DeleteAsync($"/api/restaurants/{restaurantId}/cart", Ct)).EnsureSuccessStatusCode();

        var added = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/cart/lines",
            new AddCartLineRequest(itemId, 2, null, []), Ct);
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
}
