using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Carts;
using OrderingSystem.Domain.Geography;
using OrderingSystem.Domain.Identity;
using OrderingSystem.Domain.Menu;
using OrderingSystem.Domain.Orders;
using OrderingSystem.Domain.Platform;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
    : DbContext(options), IAppDbContext
{
    private readonly ITenantContext _tenant = tenant;

    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<RestaurantStaff> RestaurantStaff => Set<RestaurantStaff>();
    public DbSet<RestaurantHours> RestaurantHours => Set<RestaurantHours>();

    public DbSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();
    public DbSet<RestaurantZone> RestaurantZones => Set<RestaurantZone>();
    public DbSet<Address> Addresses => Set<Address>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<OptionGroup> OptionGroups => Set<OptionGroup>();
    public DbSet<Option> Options => Set<Option>();
    public DbSet<MenuItemOptionGroup> MenuItemOptionGroups => Set<MenuItemOptionGroup>();

    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartLine> CartLines => Set<CartLine>();
    public DbSet<CartLineOption> CartLineOptions => Set<CartLineOption>();

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderLineOption> OrderLineOptions => Set<OrderLineOption>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OrderNumberSequence> OrderNumberSequences => Set<OrderNumberSequence>();

    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyQueryFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Every global query filter in the system, deliberately in one method rather than scattered
    /// across twenty-six configuration files. These filters are the security boundary described in
    /// ADR-07, and a boundary you cannot read as a single set is a boundary nobody audits.
    /// <para>
    /// They are applied after the entity configurations because a later HasQueryFilter replaces an
    /// earlier one rather than combining with it.
    /// </para>
    /// </summary>
    private void ApplyQueryFilters(ModelBuilder modelBuilder)
    {
        // --- soft deletes -------------------------------------------------------------
        // Order rows point at these, so they are never physically removed. Excluding them
        // here means no caller has to remember the condition.
        modelBuilder.Entity<MenuItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<OptionGroup>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Option>().HasQueryFilter(e => !e.IsDeleted);

        // The join rows must disappear with either side, or a soft-deleted item leaves links
        // pointing at something no query can return. EF warns about exactly this: a filtered
        // entity on the required end of a relationship whose other end is not filtered.
        modelBuilder.Entity<MenuItemOptionGroup>().HasQueryFilter(e =>
            !e.MenuItem.IsDeleted && !e.OptionGroup.IsDeleted);

        // --- private data: visible to its owner, its restaurant, or a platform admin ---
        //
        // Note what is NOT filtered here: menus, categories, options, opening hours and zone
        // fees carry no tenant filter at all. On a marketplace those are public - every customer
        // browses every restaurant's menu by design, so filtering them by the caller's
        // restaurant_id claim would blank the storefront for any staff member who also orders as
        // a customer. Isolation on the catalogue is a WRITE concern, enforced by explicit
        // ownership checks in the staff endpoints.

        modelBuilder.Entity<Address>().HasQueryFilter(e =>
            !e.IsDeleted && (_tenant.IsPlatformAdmin || e.UserId == _tenant.UserId));

        modelBuilder.Entity<Cart>().HasQueryFilter(e =>
            _tenant.IsPlatformAdmin || e.UserId == _tenant.UserId);

        // Staff see their restaurant's orders; a customer sees their own; an admin sees all.
        // An anonymous caller has a null UserId, which matches nothing - correct, not a bug.
        modelBuilder.Entity<Order>().HasQueryFilter(e =>
            _tenant.IsPlatformAdmin
            || (_tenant.RestaurantId != null && e.RestaurantId == _tenant.RestaurantId)
            || (_tenant.RestaurantId == null && e.CustomerId == _tenant.UserId));

        modelBuilder.Entity<RestaurantStaff>().HasQueryFilter(e =>
            _tenant.IsPlatformAdmin
            || (_tenant.RestaurantId != null && e.RestaurantId == _tenant.RestaurantId));

        // --- child rows, filtered through their parent -------------------------------
        //
        // EF does NOT apply a parent's filter to its children. Without these, filtering Order
        // protects "SELECT * FROM Orders" and does nothing at all for
        // "SELECT * FROM OrderLines" - which carries the item names and prices of every
        // restaurant on the platform. The discipline of always querying from the aggregate root
        // works right up until somebody doesn't.
        //
        // Each predicate below restates its parent's rule through a navigation. They must be
        // changed together; ChildFilterConsistencyTests fails the build if one is forgotten.

        modelBuilder.Entity<OrderLine>().HasQueryFilter(e =>
            _tenant.IsPlatformAdmin
            || (_tenant.RestaurantId != null && e.Order.RestaurantId == _tenant.RestaurantId)
            || (_tenant.RestaurantId == null && e.Order.CustomerId == _tenant.UserId));

        modelBuilder.Entity<OrderLineOption>().HasQueryFilter(e =>
            _tenant.IsPlatformAdmin
            || (_tenant.RestaurantId != null && e.OrderLine.Order.RestaurantId == _tenant.RestaurantId)
            || (_tenant.RestaurantId == null && e.OrderLine.Order.CustomerId == _tenant.UserId));

        modelBuilder.Entity<OrderEvent>().HasQueryFilter(e =>
            _tenant.IsPlatformAdmin
            || (_tenant.RestaurantId != null && e.Order.RestaurantId == _tenant.RestaurantId)
            || (_tenant.RestaurantId == null && e.Order.CustomerId == _tenant.UserId));

        modelBuilder.Entity<Payment>().HasQueryFilter(e =>
            _tenant.IsPlatformAdmin
            || (_tenant.RestaurantId != null && e.Order.RestaurantId == _tenant.RestaurantId)
            || (_tenant.RestaurantId == null && e.Order.CustomerId == _tenant.UserId));

        modelBuilder.Entity<CartLine>().HasQueryFilter(e =>
            _tenant.IsPlatformAdmin || e.Cart.UserId == _tenant.UserId);

        modelBuilder.Entity<CartLineOption>().HasQueryFilter(e =>
            _tenant.IsPlatformAdmin || e.CartLine.Cart.UserId == _tenant.UserId);
    }
}
