using Microsoft.EntityFrameworkCore;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Identity;
using OrderingSystem.Domain.Orders;
using OrderingSystem.Domain.Restaurants;
using OrderingSystem.Infrastructure.Persistence;

namespace OrderingSystem.Api.IntegrationTests.Tenancy;

/// <summary>
/// Two restaurants, each with a staff member and a customer who has ordered from them. The whole
/// point of the marketplace's security model is that neither side can see the other, so the tests
/// need both sides to exist before they can prove anything.
/// </summary>
public sealed class TwoRestaurantScenario : IAsyncLifetime
{
    private readonly SqlServerFixture _database = new();

    public Guid RestaurantA { get; } = Guid.NewGuid();
    public Guid RestaurantB { get; } = Guid.NewGuid();
    public Guid StaffA { get; } = Guid.NewGuid();
    public Guid StaffB { get; } = Guid.NewGuid();
    public Guid CustomerA { get; } = Guid.NewGuid();
    public Guid CustomerB { get; } = Guid.NewGuid();
    public Guid OrderA { get; private set; }
    public Guid OrderB { get; private set; }

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();

        await using var db = Context(TestTenant.PlatformAdmin());

        AddRestaurant(db, RestaurantA, "Restaurant A");
        AddRestaurant(db, RestaurantB, "Restaurant B");
        AddUser(db, StaffA, "staff-a");
        AddUser(db, StaffB, "staff-b");
        AddUser(db, CustomerA, "customer-a");
        AddUser(db, CustomerB, "customer-b");
        await db.SaveChangesAsync();

        db.RestaurantStaff.Add(new RestaurantStaff
        {
            UserId = StaffA, RestaurantId = RestaurantA,
            StaffRole = StaffRoleType.Staff, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.RestaurantStaff.Add(new RestaurantStaff
        {
            UserId = StaffB, RestaurantId = RestaurantB,
            StaffRole = StaffRoleType.Staff, CreatedAt = DateTimeOffset.UtcNow,
        });

        OrderA = AddOrder(db, CustomerA, RestaurantA, "A-0001");
        OrderB = AddOrder(db, CustomerB, RestaurantB, "B-0001");
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    public AppDbContext Context(TestTenant tenant) => _database.CreateContext(tenant);

    private static void AddRestaurant(AppDbContext db, Guid id, string name) =>
        db.Restaurants.Add(new Restaurant
        {
            Id = id,
            Name = name,
            Slug = $"{name.ToLowerInvariant().Replace(' ', '-')}-{id:N}"[..40],
            Phone = "+96170000000",
            IsActive = true,
            CommissionPercent = 15m,
            MinOrderUsd = 5m,
            DefaultPrepMinutes = 20,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static void AddUser(AppDbContext db, Guid id, string handle) =>
        db.Users.Add(new User
        {
            Id = id,
            Email = $"{handle}-{id:N}@example.test",
            PasswordHash = "not-a-real-hash",
            FullName = handle,
            Phone = "+96170000000",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static Guid AddOrder(
        AppDbContext db, Guid customerId, Guid restaurantId, string number)
    {
        var orderId = Guid.NewGuid();

        db.Orders.Add(new Order
        {
            Id = orderId,
            OrderNumber = $"{number}-{Guid.NewGuid():N}"[..16],
            CustomerId = customerId,
            RestaurantId = restaurantId,
            FulfillmentType = FulfillmentType.Pickup,
            Status = OrderStatus.Placed,
            PaymentMethod = PaymentMethod.CashOnDelivery,
            PaymentStatus = PaymentStatus.Pending,
            SubtotalUsd = 20m,
            TotalUsd = 20m,
            ExchangeRateLbp = 89_500m,
            CommissionPercent = 15m,
            CommissionUsd = 3m,
            PromisedMinutesMin = 20,
            PromisedMinutesMax = 30,
            IdempotencyKey = Guid.NewGuid(),
            PlacedAt = DateTimeOffset.UtcNow,
        });

        // The child rows are the point: filtering Orders alone leaves these wide open.
        db.OrderLines.Add(new OrderLine
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ItemNameSnapshot = $"Secret recipe of {number}",
            UnitPriceUsd = 20m,
            Quantity = 1,
            LineTotalUsd = 20m,
        });

        db.OrderEvents.Add(new OrderEvent
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ToStatus = OrderStatus.Placed,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Method = PaymentMethod.CashOnDelivery,
            AmountUsd = 20m,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return orderId;
    }
}
