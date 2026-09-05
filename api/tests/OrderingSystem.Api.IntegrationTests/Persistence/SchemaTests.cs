using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Geography;
using OrderingSystem.Domain.Identity;
using OrderingSystem.Domain.Orders;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Api.IntegrationTests.Persistence;

/// <summary>
/// Proves the migration produces the schema the configuration describes, and — more importantly —
/// that the database refuses the things it is supposed to refuse. Every assertion here fails on
/// the EF in-memory provider regardless of whether the code is correct, which is why these run
/// against a real SQL Server.
/// </summary>
public sealed class SchemaTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture = fixture;

    /// <summary>xUnit v3 wants every awaitable call to honour the test's cancellation token.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migration_creates_every_table()
    {
        var count = await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name <> '__EFMigrationsHistory' AND is_ms_shipped = 0");

        count.ShouldBe(26);
    }

    [Fact]
    public async Task No_string_column_is_left_unbounded()
    {
        // A string property without an explicit length silently becomes nvarchar(max), which
        // cannot be indexed and is stored off-row. One of these is a performance bug nobody
        // notices until the table is large.
        var unbounded = await ScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.columns c
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            JOIN sys.tables t ON t.object_id = c.object_id
            WHERE ty.name = 'nvarchar' AND c.max_length = -1 AND t.is_ms_shipped = 0
            """);

        unbounded.ShouldBe(0);
    }

    [Fact]
    public async Task Money_columns_are_decimal_10_2()
    {
        var wrong = await ScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.columns c
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID('Orders')
              AND c.name LIKE '%Usd'
              AND NOT (ty.name = 'decimal' AND c.precision = 10 AND c.scale = 2)
            """);

        wrong.ShouldBe(0, "every USD column on Orders must be decimal(10,2), never float");
    }

    [Fact]
    public async Task Duplicate_idempotency_key_is_rejected()
    {
        var (customerId, restaurantId) = await SeedAsync();
        var key = Guid.NewGuid();

        await using var db = _fixture.CreateContext(TestTenant.PlatformAdmin());
        db.Orders.Add(NewOrder(customerId, restaurantId, key));
        await db.SaveChangesAsync(Ct);

        // The same checkout submitted twice — a double tap on a poor connection.
        db.Orders.Add(NewOrder(customerId, restaurantId, key));

        var thrown = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        SqlErrorNumber(thrown).ShouldBe(2601, "a unique index violation, not some other failure");
    }

    [Fact]
    public async Task Rejecting_an_order_without_a_reason_is_refused()
    {
        var (customerId, restaurantId) = await SeedAsync();

        await using var db = _fixture.CreateContext(TestTenant.PlatformAdmin());
        var order = NewOrder(customerId, restaurantId, Guid.NewGuid());
        order.Status = OrderStatus.Rejected;
        order.RejectionReason = null;
        db.Orders.Add(order);

        var thrown = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        SqlErrorNumber(thrown).ShouldBe(547, "CK_Orders_RejectionReasonRequired should have blocked this");
    }

    [Fact]
    public async Task A_customer_cannot_hold_two_default_addresses()
    {
        var (customerId, _) = await SeedAsync();
        var tenant = TestTenant.Customer(customerId);

        await using var db = _fixture.CreateContext(tenant);
        var zone = new DeliveryZone { Id = Guid.NewGuid(), Name = $"Zone {Guid.NewGuid():N}" };
        db.DeliveryZones.Add(zone);
        db.Addresses.Add(NewAddress(customerId, zone.Id));
        await db.SaveChangesAsync(Ct);

        db.Addresses.Add(NewAddress(customerId, zone.Id));

        var thrown = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        SqlErrorNumber(thrown).ShouldBe(2601, "the filtered unique index should allow only one default");
    }

    [Fact]
    public async Task A_customer_may_keep_one_default_and_many_others()
    {
        var (customerId, _) = await SeedAsync();

        await using var db = _fixture.CreateContext(TestTenant.Customer(customerId));
        var zone = new DeliveryZone { Id = Guid.NewGuid(), Name = $"Zone {Guid.NewGuid():N}" };
        db.DeliveryZones.Add(zone);

        db.Addresses.Add(NewAddress(customerId, zone.Id, isDefault: true));
        db.Addresses.Add(NewAddress(customerId, zone.Id, isDefault: false));
        db.Addresses.Add(NewAddress(customerId, zone.Id, isDefault: false));

        await Should.NotThrowAsync(() => db.SaveChangesAsync(Ct));
    }

    // ------------------------------------------------------------------ helpers

    private async Task<(Guid CustomerId, Guid RestaurantId)> SeedAsync()
    {
        await using var db = _fixture.CreateContext(TestTenant.PlatformAdmin());

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.com",
            PasswordHash = "not-a-real-hash",
            FullName = "Test Customer",
            Phone = "+96170000000",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var restaurant = new Restaurant
        {
            Id = Guid.NewGuid(),
            Name = "Test Kitchen",
            Slug = $"test-{Guid.NewGuid():N}",
            Phone = "+96170000001",
            IsActive = true,
            CommissionPercent = 15m,
            MinOrderUsd = 5m,
            DefaultPrepMinutes = 20,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Users.Add(user);
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync(Ct);

        return (user.Id, restaurant.Id);
    }

    private static Order NewOrder(Guid customerId, Guid restaurantId, Guid idempotencyKey) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = $"TK-{Guid.NewGuid():N}"[..16],
        CustomerId = customerId,
        RestaurantId = restaurantId,
        FulfillmentType = FulfillmentType.Pickup,
        Status = OrderStatus.Placed,
        PaymentMethod = PaymentMethod.CashOnDelivery,
        PaymentStatus = PaymentStatus.Pending,
        SubtotalUsd = 12.50m,
        TotalUsd = 12.50m,
        ExchangeRateLbp = 89500m,
        CommissionPercent = 15m,
        CommissionUsd = 1.88m,
        PromisedMinutesMin = 20,
        PromisedMinutesMax = 30,
        IdempotencyKey = idempotencyKey,
        PlacedAt = DateTimeOffset.UtcNow,
        BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

    private static Address NewAddress(Guid userId, Guid zoneId, bool isDefault = true) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        ZoneId = zoneId,
        Label = "Home",
        Line1 = "Rue Gouraud",
        Landmark = "Above the pharmacy",
        IsDefault = isDefault,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>The SQL Server error number underneath an EF wrapper, so a test can name the rule it expects to fire.</summary>
    private static int SqlErrorNumber(Exception exception) =>
        exception.InnerException is SqlException sql ? sql.Number : -1;

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(Ct);
        return (T)Convert.ChangeType(result!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}
