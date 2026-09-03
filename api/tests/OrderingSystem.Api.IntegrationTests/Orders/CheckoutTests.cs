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
/// Turning a basket into an order, and the several ways that is refused.
/// <para>
/// The refusals matter as much as the success. Each one is a different thing gone wrong with a
/// different thing a person can do about it, and collapsing them into one "cannot check out"
/// would leave a customer stuck with no idea which.
/// </para>
/// </summary>
public sealed class CheckoutTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Matches how the API serialises, and cached because the analyzer is right.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions Json =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_pickup_order_is_placed_and_priced_by_the_server()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");
        var order = await CheckoutAsync(client, restaurant.Id,
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, "no ketchup", quote.TotalUsd, Guid.NewGuid()));

        order.Status.ShouldBe(OrderStatus.Placed);
        order.TotalUsd.ShouldBe(quote.TotalUsd);
        order.SubtotalUsd.ShouldBe(quote.SubtotalUsd);
        order.DeliveryFeeUsd.ShouldBe(0m);
        order.PaymentStatus.ShouldBe(PaymentStatus.Pending);

        // The reference a kitchen calls out.
        order.OrderNumber.ShouldStartWith("FRIESLAB-");
        order.OrderNumber.Length.ShouldBeLessThanOrEqualTo(32);
    }

    [Fact]
    public async Task Placing_an_order_empties_the_basket()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");
        await CheckoutAsync(client, restaurant.Id,
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, Guid.NewGuid()));

        // Leaving it would let a second checkout place the same food twice.
        var cart = await client.GetFromJsonAsync<CartResponse>(
            $"/api/restaurants/{restaurant.Id}/cart", Ct);
        cart!.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_order_keeps_its_own_copy_of_names_and_prices()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Truffle Parmesan Fries", 1);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");
        var placed = await CheckoutAsync(client, restaurant.Id,
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, Guid.NewGuid()));

        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var line = await db.OrderLines.Where(l => l.OrderId == placed.Id).FirstAsync(Ct);

        // Text and numbers, not lookups. Renaming the dish next week must not restate what this
        // customer bought today.
        line.ItemNameSnapshot.ShouldBe("Truffle Parmesan Fries");
        line.UnitPriceUsd.ShouldBeGreaterThan(0m);
        line.LineTotalUsd.ShouldBe(line.UnitPriceUsd * line.Quantity);
    }

    [Fact]
    public async Task Chosen_options_are_copied_with_their_group_name()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();

        await ClearAsync(client, restaurant.Id);
        var burger = await ItemAsync("Classic Smash");
        var large = await OptionAsync("Large");
        await AddAsync(client, restaurant.Id, burger.Id, 1, [new ChosenOptionRequest(large.Id, 1)]);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");
        var placed = await CheckoutAsync(client, restaurant.Id,
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, Guid.NewGuid()));

        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var option = await db.OrderLineOptions
            .Where(o => o.OrderLine.OrderId == placed.Id).FirstAsync(Ct);

        // "Size: Large" still reads correctly after the group is renamed or deleted.
        option.GroupNameSnapshot.ShouldBe("Size");
        option.OptionNameSnapshot.ShouldBe("Large");
    }

    [Fact]
    public async Task The_first_event_records_who_placed_it()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");
        var placed = await CheckoutAsync(client, restaurant.Id,
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, Guid.NewGuid()));

        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var events = await db.OrderEvents.Where(e => e.OrderId == placed.Id).ToListAsync(Ct);

        var first = events.ShouldHaveSingleItem();
        first.FromStatus.ShouldBeNull("an order comes from nowhere");
        first.ToStatus.ShouldBe(OrderStatus.Placed);
        first.ChangedByUserId.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_delivery_order_copies_the_address_rather_than_pointing_at_it()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 3);

        var address = await ServedAddressAsync("rita@example.test", restaurant.Id);
        var quote = await QuoteAsync(client, restaurant.Id, "Delivery", address.Id);
        var placed = await CheckoutAsync(client, restaurant.Id,
            new CheckoutRequest(FulfillmentType.Delivery, address.Id, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, Guid.NewGuid()));

        placed.DeliveryFeeUsd.ShouldBe(address.FeeUsd);

        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var order = await db.Orders.Where(o => o.Id == placed.Id).FirstAsync(Ct);

        // The customer may delete this address tomorrow; the courier still has to know where the
        // food went today.
        order.DeliveryZoneName.ShouldBe(address.ZoneName);
        order.DeliveryLine1.ShouldNotBeNullOrWhiteSpace();
        order.AddressId.ShouldBe(address.Id);
    }

    [Fact]
    public async Task Order_numbers_count_up_within_a_day()
    {
        var client = await SignInAsync("joe@example.test");
        var restaurant = await RestaurantAsync();

        var numbers = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);
            var quote = await QuoteAsync(client, restaurant.Id, "Pickup");
            var placed = await CheckoutAsync(client, restaurant.Id,
                new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, Guid.NewGuid()));
            numbers.Add(placed.OrderNumber);
        }

        // Distinct, and allocated one at a time rather than two customers sharing a number.
        numbers.Distinct().Count().ShouldBe(3);
    }

    // ------------------------------------------------------------------ the refusals

    [Fact]
    public async Task An_empty_basket_cannot_be_ordered()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await ClearAsync(client, restaurant.Id);

        var response = await PostCheckoutAsync(client, restaurant.Id,
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, 0m, Guid.NewGuid()));

        response.Status.ShouldBe(HttpStatusCode.Conflict);
        response.Body.ShouldContain("empty");
    }

    [Fact]
    public async Task A_basket_below_the_minimum_says_how_much_more_is_needed()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();

        await ClearAsync(client, restaurant.Id);
        var coke = await ItemAsync("Coca-Cola");
        await AddAsync(client, restaurant.Id, coke.Id, 1, []);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");
        quote.MeetsMinimum.ShouldBeFalse("the seed's minimum is above the price of one drink");

        var response = await PostCheckoutAsync(client, restaurant.Id,
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, Guid.NewGuid()));

        response.Status.ShouldBe(HttpStatusCode.Conflict);
        response.Body.ShouldContain("minimum");
        // Names the gap, so the screen can say what to do rather than only that it failed.
        response.Body.ShouldContain("Add $");

        await ClearAsync(client, restaurant.Id);
    }

    [Fact]
    public async Task A_total_that_moved_since_the_quote_stops_the_checkout()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");

        // Standing in for a price that changed while the customer read the menu.
        var response = await PostCheckoutAsync(client, restaurant.Id,
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null,
                quote.TotalUsd - 1.00m, Guid.NewGuid()));

        response.Status.ShouldBe(HttpStatusCode.Conflict);

        // Both numbers, so the customer can see what changed rather than being told to try again.
        response.Body.ShouldContain(quote.TotalUsd.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

        await ClearAsync(client, restaurant.Id);
    }

    [Fact]
    public async Task A_customer_cannot_name_their_own_price()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);

        var response = await PostCheckoutAsync(client, restaurant.Id,
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, 0.01m, Guid.NewGuid()));

        // The expected total is a statement of what they agreed to, never an instruction.
        response.Status.ShouldBe(HttpStatusCode.Conflict);

        await ClearAsync(client, restaurant.Id);
    }

    [Fact]
    public async Task A_dish_that_sold_out_while_the_basket_sat_is_named()
    {
        var client = await SignInAsync("rita@example.test");
        var staff = await SignInAsync("owner@frieslab.test");
        var restaurant = await RestaurantAsync();

        await ClearAsync(client, restaurant.Id);
        var item = await ItemAsync("Chili Cheese Fries");
        await AddAsync(client, restaurant.Id, item.Id, 3, []);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");

        // The kitchen runs out between the basket and the checkout.
        var hidden = await staff.PatchAsJsonAsync(
            $"/api/restaurant/menu-items/{item.Id}/availability", new { isAvailable = false }, Ct);
        hidden.EnsureSuccessStatusCode();

        try
        {
            var response = await PostCheckoutAsync(client, restaurant.Id,
                new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, Guid.NewGuid()));

            response.Status.ShouldBe(HttpStatusCode.Conflict);
            response.Body.ShouldContain("Chili Cheese Fries");
        }
        finally
        {
            await staff.PatchAsJsonAsync(
                $"/api/restaurant/menu-items/{item.Id}/availability", new { isAvailable = true }, Ct);
            await ClearAsync(client, restaurant.Id);
        }
    }

    [Fact]
    public async Task A_restaurant_that_paused_orders_says_so_rather_than_saying_closed()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);
        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");

        await SetAcceptingOrdersAsync(restaurant.Id, false);

        try
        {
            var response = await PostCheckoutAsync(client, restaurant.Id,
                new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, Guid.NewGuid()));

            response.Status.ShouldBe(HttpStatusCode.Conflict);

            // "Closed" would send somebody away to come back at opening time and find it still
            // paused. They are different problems.
            response.Body.ShouldContain("paused");
        }
        finally
        {
            await SetAcceptingOrdersAsync(restaurant.Id, true);
            await ClearAsync(client, restaurant.Id);
        }
    }

    [Fact]
    public async Task Delivering_without_an_address_is_refused()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);

        var response = await PostCheckoutAsync(client, restaurant.Id,
            new CheckoutRequest(FulfillmentType.Delivery, null, PaymentMethod.CashOnDelivery, null, 100m, Guid.NewGuid()));

        response.Status.ShouldBe(HttpStatusCode.BadRequest);
        response.Body.ShouldContain("addressId");

        await ClearAsync(client, restaurant.Id);
    }

    [Fact]
    public async Task Somebody_elses_address_cannot_be_delivered_to()
    {
        var client = await SignInAsync("joe@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 3);

        var ritas = await ServedAddressAsync("rita@example.test", restaurant.Id);

        var response = await PostCheckoutAsync(client, restaurant.Id,
            new CheckoutRequest(FulfillmentType.Delivery, ritas.Id, PaymentMethod.CashOnDelivery, null, 100m, Guid.NewGuid()));

        response.Status.ShouldBe(HttpStatusCode.NotFound);

        await ClearAsync(client, restaurant.Id);
    }

    [Fact]
    public async Task Checking_out_needs_somebody_to_be_signed_in()
    {
        var restaurant = await RestaurantAsync();

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/restaurants/{restaurant.Id}/orders",
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, 0m, Guid.NewGuid()), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_closed_restaurant_says_so_rather_than_taking_the_order()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);
        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");

        // FriesLab runs from noon until two in the morning, so nine is firmly shut. Writing this
        // test needed a clock the test could move — before that it either passed or failed
        // depending on the hour the suite happened to run.
        factory.Clock.LocalTimeOfDay = new TimeOnly(9, 0);

        try
        {
            var response = await PostCheckoutAsync(client, restaurant.Id,
                new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, Guid.NewGuid()));

            response.Status.ShouldBe(HttpStatusCode.Conflict);
            response.Body.ShouldContain("closed");
        }
        finally
        {
            factory.Clock.LocalTimeOfDay = TestClock.DefaultLocalTime;
            await ClearAsync(client, restaurant.Id);
        }
    }

    [Fact]
    public async Task An_order_placed_after_midnight_still_belongs_to_that_evening_service()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);
        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");

        // One in the morning: FriesLab's window opened at noon the day before and runs to two.
        factory.Clock.LocalTimeOfDay = new TimeOnly(1, 0);

        try
        {
            var placed = await CheckoutAsync(client, restaurant.Id,
                new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, Guid.NewGuid()));

            // Open, so the order goes through. The number carries the calendar date rather than
            // the shift's — a kitchen open past midnight sees its numbering reset mid-service,
            // which is the trade OrderNumberSequence documents.
            placed.Status.ShouldBe(OrderStatus.Placed);
            placed.OrderNumber.ShouldContain(factory.Clock.LocalToday.ToString("yyMMdd", System.Globalization.CultureInfo.InvariantCulture));
        }
        finally
        {
            factory.Clock.LocalTimeOfDay = TestClock.DefaultLocalTime;
            await ClearAsync(client, restaurant.Id);
        }
    }

    // ------------------------------------------------------------------ the double tap

    [Fact]
    public async Task Placing_the_same_attempt_twice_returns_the_first_order()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");
        var key = Guid.NewGuid();
        var request = new CheckoutRequest(
            FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, key);

        var first = await CheckoutAsync(client, restaurant.Id, request);

        // The customer saw nothing happen and tapped again. Note that the basket is empty by now,
        // so without the key this would be refused — and the customer told their order failed
        // when it had in fact succeeded.
        var second = await CheckoutAsync(client, restaurant.Id, request);

        second.Id.ShouldBe(first.Id);
        second.OrderNumber.ShouldBe(first.OrderNumber);

        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var count = await db.Orders.CountAsync(o => o.IdempotencyKey == key, Ct);
        count.ShouldBe(1, "a double tap must not cook the same food twice");
    }

    [Fact]
    public async Task Two_different_attempts_are_two_orders()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();

        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);
        var firstQuote = await QuoteAsync(client, restaurant.Id, "Pickup");
        var first = await CheckoutAsync(client, restaurant.Id, new CheckoutRequest(
            FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, firstQuote.TotalUsd, Guid.NewGuid()));

        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);
        var secondQuote = await QuoteAsync(client, restaurant.Id, "Pickup");
        var second = await CheckoutAsync(client, restaurant.Id, new CheckoutRequest(
            FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, secondQuote.TotalUsd, Guid.NewGuid()));

        // Ordering the same thing twice on purpose is a normal thing to do.
        second.Id.ShouldNotBe(first.Id);
        second.OrderNumber.ShouldNotBe(first.OrderNumber);
    }

    [Fact]
    public async Task Somebody_elses_idempotency_key_reveals_nothing()
    {
        var rita = await SignInAsync("rita@example.test");
        var joe = await SignInAsync("joe@example.test");
        var restaurant = await RestaurantAsync();

        await StockBasketAsync(rita, restaurant.Id, "Cheese Lab Fries", 2);
        var quote = await QuoteAsync(rita, restaurant.Id, "Pickup");
        var key = Guid.NewGuid();
        var ritas = await CheckoutAsync(rita, restaurant.Id, new CheckoutRequest(
            FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, key));

        // Keys are client-generated. Answering for anybody's order would make this a way to read
        // a stranger's by guessing one.
        await ClearAsync(joe, restaurant.Id);
        var response = await PostCheckoutAsync(joe, restaurant.Id, new CheckoutRequest(
            FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, 0m, key));

        response.Status.ShouldBe(HttpStatusCode.Conflict, "Joe's basket is empty, so this is refused");
        response.Body.ShouldNotContain(ritas.OrderNumber);
    }

    [Fact]
    public async Task A_checkout_without_an_idempotency_key_is_refused()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");
        var response = await PostCheckoutAsync(client, restaurant.Id, new CheckoutRequest(
            FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, quote.TotalUsd, Guid.Empty));

        // The unique index has no filter, so an omitted key becomes Guid.Empty and the second
        // order ever placed collides with the first.
        response.Status.ShouldBe(HttpStatusCode.BadRequest);

        await ClearAsync(client, restaurant.Id);
    }

    [Fact]
    public async Task A_note_longer_than_the_column_is_refused_rather_than_truncated()
    {
        var client = await SignInAsync("rita@example.test");
        var restaurant = await RestaurantAsync();
        await StockBasketAsync(client, restaurant.Id, "Cheese Lab Fries", 2);

        var quote = await QuoteAsync(client, restaurant.Id, "Pickup");
        var response = await PostCheckoutAsync(client, restaurant.Id, new CheckoutRequest(
            FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, new string('x', 501),
            quote.TotalUsd, Guid.NewGuid()));

        // A 400 naming the field, not a 500 from SQL Server refusing to truncate. The validator
        // allowed 1000 while the column held 500, so this was the latter until the two were
        // lined up — a customer with a long note losing their order to an error saying nothing.
        response.Status.ShouldBe(HttpStatusCode.BadRequest);
        response.Body.ShouldContain("CustomerNote", Case.Insensitive);

        await ClearAsync(client, restaurant.Id);
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

    /// <summary>A basket comfortably over the minimum, so a test about something else is not about that.</summary>
    private async Task StockBasketAsync(HttpClient client, Guid restaurantId, string itemName, int quantity)
    {
        await ClearAsync(client, restaurantId);
        var item = await ItemAsync(itemName);
        await AddAsync(client, restaurantId, item.Id, quantity, []);
    }

    private static async Task AddAsync(
        HttpClient client, Guid restaurantId, Guid itemId, int quantity,
        IReadOnlyList<ChosenOptionRequest> options)
    {
        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/cart/lines",
            new AddCartLineRequest(itemId, quantity, null, options), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"adding failed with {(int)response.StatusCode}: {body}");
    }

    private static async Task ClearAsync(HttpClient client, Guid restaurantId) =>
        (await client.DeleteAsync($"/api/restaurants/{restaurantId}/cart", Ct)).EnsureSuccessStatusCode();

    private static async Task<QuoteResponse> QuoteAsync(
        HttpClient client, Guid restaurantId, string fulfillment, Guid? addressId = null)
    {
        var url = $"/api/restaurants/{restaurantId}/cart/quote?fulfillment={fulfillment}"
            + (addressId is null ? string.Empty : $"&addressId={addressId}");

        var response = await client.GetAsync(url, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"the quote failed with {(int)response.StatusCode}: {body}");

        return (await response.Content.ReadFromJsonAsync<QuoteResponse>(Ct))!;
    }

    private static async Task<PlacedOrderResponse> CheckoutAsync(
        HttpClient client, Guid restaurantId, CheckoutRequest request)
    {
        var result = await PostCheckoutAsync(client, restaurantId, request);
        ((int)result.Status).ShouldBe(201, $"checkout failed: {result.Body}");

        return System.Text.Json.JsonSerializer.Deserialize<PlacedOrderResponse>(result.Body, Json)!;
    }

    /// <summary>Posts and hands back both status and body, because the body is where the reason is.</summary>
    private static async Task<(HttpStatusCode Status, string Body)> PostCheckoutAsync(
        HttpClient client, Guid restaurantId, CheckoutRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/restaurants/{restaurantId}/orders", request, Ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<(Guid Id, string Slug)> RestaurantAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var row = await db.Restaurants.Where(r => r.Slug == "frieslab")
            .Select(r => new { r.Id, r.Slug }).FirstAsync(Ct);
        return (row.Id, row.Slug);
    }

    private async Task<(Guid Id, decimal Price)> ItemAsync(string name)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var row = await db.MenuItems
            .Where(i => i.Restaurant.Slug == "frieslab" && i.Name == name)
            .Select(i => new { i.Id, i.BasePriceUsd }).FirstAsync(Ct);
        return (row.Id, row.BasePriceUsd);
    }

    private async Task<(Guid Id, decimal Delta)> OptionAsync(string name)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var row = await db.Options
            .Where(o => o.OptionGroup.Restaurant.Slug == "frieslab" && o.Name == name)
            .Select(o => new { o.Id, o.PriceDeltaUsd }).FirstAsync(Ct);
        return (row.Id, row.PriceDeltaUsd);
    }

    private async Task<(Guid Id, string ZoneName, decimal FeeUsd)> ServedAddressAsync(
        string email, Guid restaurantId)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        var row = await (
            from address in db.Addresses
            join zone in db.RestaurantZones on address.ZoneId equals zone.ZoneId
            where address.User.Email == email && zone.RestaurantId == restaurantId && zone.IsActive
            select new { address.Id, ZoneName = address.Zone.Name, zone.DeliveryFeeUsd })
            .FirstAsync(Ct);

        return (row.Id, row.ZoneName, row.DeliveryFeeUsd);
    }

    private async Task SetAcceptingOrdersAsync(Guid restaurantId, bool accepting)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        var restaurant = await db.Restaurants.FirstAsync(r => r.Id == restaurantId, Ct);
        restaurant.IsAcceptingOrders = accepting;
        await db.SaveChangesAsync(Ct);
    }
}
