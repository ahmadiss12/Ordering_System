using Microsoft.EntityFrameworkCore;
using OrderingSystem.Domain.Carts;
using OrderingSystem.Domain.Geography;
using OrderingSystem.Domain.Identity;
using OrderingSystem.Domain.Menu;
using OrderingSystem.Domain.Orders;
using OrderingSystem.Domain.Platform;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Application.Abstractions;

/// <summary>
/// The database, as the application sees it. Exposing DbSet rather than a repository per entity is
/// deliberate — see ADR-06: this keeps Include, projection, split queries and AsNoTracking
/// available, which a generic repository would take away.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }

    DbSet<Restaurant> Restaurants { get; }
    DbSet<RestaurantStaff> RestaurantStaff { get; }
    DbSet<RestaurantHours> RestaurantHours { get; }

    DbSet<DeliveryZone> DeliveryZones { get; }
    DbSet<RestaurantZone> RestaurantZones { get; }
    DbSet<Address> Addresses { get; }

    DbSet<Category> Categories { get; }
    DbSet<MenuItem> MenuItems { get; }
    DbSet<OptionGroup> OptionGroups { get; }
    DbSet<Option> Options { get; }
    DbSet<MenuItemOptionGroup> MenuItemOptionGroups { get; }

    DbSet<Cart> Carts { get; }
    DbSet<CartLine> CartLines { get; }
    DbSet<CartLineOption> CartLineOptions { get; }

    DbSet<Order> Orders { get; }
    DbSet<OrderLine> OrderLines { get; }
    DbSet<OrderLineOption> OrderLineOptions { get; }
    DbSet<OrderEvent> OrderEvents { get; }
    DbSet<Payment> Payments { get; }
    DbSet<OrderNumberSequence> OrderNumberSequences { get; }

    DbSet<ExchangeRate> ExchangeRates { get; }
    DbSet<PlatformSetting> PlatformSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
