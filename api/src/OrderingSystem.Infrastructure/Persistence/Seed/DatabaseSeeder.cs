using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Geography;
using OrderingSystem.Domain.Identity;
using OrderingSystem.Domain.Menu;
using OrderingSystem.Domain.Platform;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Infrastructure.Persistence.Seed;

/// <summary>
/// Fills an empty database with a believable Lebanese marketplace: three restaurants, real menus,
/// option groups covering every selection shape, delivery zones with per-restaurant fees, and
/// accounts for each role.
/// <para>
/// Idempotent — every insert is guarded by an existence check, so running it twice changes
/// nothing. Combined with the deterministic ids in <see cref="SeedIds"/>, that means you can seed
/// repeatedly without the demo shifting under you.
/// </para>
/// <para>
/// Run explicitly (<c>dotnet run -- --seed</c>), never automatically on startup. A seeder that
/// runs itself eventually runs somewhere it should not.
/// </para>
/// </summary>
public sealed class DatabaseSeeder(AppDbContext db, ILogger<DatabaseSeeder> logger)
{
    /// <summary>The password every seeded account shares. Development data only.</summary>
    public const string SeedPassword = "Passw0rd!";

    private readonly PasswordHasher<User> _hasher = new();

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Seeding database...");

        await SeedPlatformSettingsAsync(cancellationToken);
        await SeedZonesAsync(cancellationToken);
        var adminId = await SeedUsersAsync(cancellationToken);
        await SeedExchangeRateAsync(adminId, cancellationToken);
        await SeedRestaurantsAsync(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seed complete: {Restaurants} restaurants, {Items} menu items, {Zones} zones, {Users} users.",
            await db.Restaurants.CountAsync(cancellationToken),
            await db.MenuItems.CountAsync(cancellationToken),
            await db.DeliveryZones.CountAsync(cancellationToken),
            await db.Users.CountAsync(cancellationToken));
    }

    // ---------------------------------------------------------------- platform

    private async Task SeedPlatformSettingsAsync(CancellationToken ct)
    {
        await AddIfMissingAsync(db.PlatformSettings, PlatformSettingKeys.DefaultCommissionPercent,
            () => new PlatformSetting
            {
                Key = PlatformSettingKeys.DefaultCommissionPercent,
                Value = "15.00",
                UpdatedAt = DateTimeOffset.UtcNow,
            }, ct);

        await AddIfMissingAsync(db.PlatformSettings, PlatformSettingKeys.StaleOrderThresholdMinutes,
            () => new PlatformSetting
            {
                Key = PlatformSettingKeys.StaleOrderThresholdMinutes,
                Value = "10",
                UpdatedAt = DateTimeOffset.UtcNow,
            }, ct);
    }

    private async Task SeedExchangeRateAsync(Guid adminId, CancellationToken ct)
    {
        if (await db.ExchangeRates.AnyAsync(ct))
        {
            return;
        }

        db.ExchangeRates.Add(new ExchangeRate
        {
            Id = SeedIds.From("rate:initial"),
            RateLbpPerUsd = 89_500m,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-30),
            SetByUserId = adminId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private async Task SeedZonesAsync(CancellationToken ct)
    {
        foreach (var name in MenuCatalog.Zones)
        {
            var id = SeedIds.From($"zone:{name}");
            if (await db.DeliveryZones.AnyAsync(z => z.Id == id, ct))
            {
                continue;
            }

            db.DeliveryZones.Add(new DeliveryZone { Id = id, Name = name, IsActive = true });
        }

        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- people

    private async Task<Guid> SeedUsersAsync(CancellationToken ct)
    {
        var admin = await AddUserAsync("admin@ordering.test", "Platform Admin", RoleType.PlatformAdmin, ct);

        await AddUserAsync("owner@frieslab.test", "Nadia Owner", RoleType.RestaurantOwner, ct);
        await AddUserAsync("staff@frieslab.test", "Sami Staff", RoleType.RestaurantStaff, ct);
        await AddUserAsync("owner@mezze.test", "Karim Owner", RoleType.RestaurantOwner, ct);
        await AddUserAsync("owner@shawarma.test", "Layla Owner", RoleType.RestaurantOwner, ct);

        var rita = await AddUserAsync("rita@example.test", "Rita Customer", RoleType.Customer, ct);
        await AddUserAsync("joe@example.test", "Joe Customer", RoleType.Customer, ct);

        await db.SaveChangesAsync(ct);
        await AddAddressesAsync(rita, ct);

        return admin;
    }

    private async Task<Guid> AddUserAsync(string email, string fullName, RoleType role, CancellationToken ct)
    {
        var id = SeedIds.From($"user:{email}");
        if (await db.Users.AnyAsync(u => u.Id == id, ct))
        {
            return id;
        }

        var user = new User
        {
            Id = id,
            Email = email,
            FullName = fullName,
            Phone = "+96170000000",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = _hasher.HashPassword(user, SeedPassword);

        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = id, Role = role });

        // Everyone can also order as a customer, which is the case the spec explicitly allows.
        if (role != RoleType.Customer)
        {
            db.UserRoles.Add(new UserRole { UserId = id, Role = RoleType.Customer });
        }

        return id;
    }

    private async Task AddAddressesAsync(Guid userId, CancellationToken ct)
    {
        // IgnoreQueryFilters: the seeder runs with no signed-in user, so the ownership filter on
        // Address would hide these rows from its own existence check.
        if (await db.Addresses.IgnoreQueryFilters().AnyAsync(a => a.UserId == userId, ct))
        {
            return;
        }

        db.Addresses.Add(new Address
        {
            Id = SeedIds.From($"address:{userId}:home"),
            UserId = userId,
            ZoneId = SeedIds.From("zone:Achrafieh"),
            Label = "Home",
            Line1 = "Rue Sassine, Building Aoun",
            Floor = "4th floor",
            Landmark = "Above the pharmacy, opposite the church",
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Addresses.Add(new Address
        {
            Id = SeedIds.From($"address:{userId}:work"),
            UserId = userId,
            ZoneId = SeedIds.From("zone:Hamra"),
            Label = "Work",
            Line1 = "Rue Bliss",
            Landmark = "Next to the AUB main gate",
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- restaurants

    private async Task SeedRestaurantsAsync(CancellationToken ct)
    {
        foreach (var seed in MenuCatalog.Restaurants)
        {
            var restaurantId = SeedIds.From($"restaurant:{seed.Slug}");
            if (await db.Restaurants.AnyAsync(r => r.Id == restaurantId, ct))
            {
                continue;
            }

            db.Restaurants.Add(new Restaurant
            {
                Id = restaurantId,
                Name = seed.Name,
                Slug = seed.Slug,
                Description = seed.Description,
                Phone = seed.Phone,
                IsActive = true,
                IsAcceptingOrders = true,
                CommissionPercent = seed.CommissionPercent,
                MinOrderUsd = seed.MinOrderUsd,
                DefaultPrepMinutes = seed.PrepMinutes,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            AddHours(restaurantId, seed.Slug);
            AddZones(restaurantId, seed);
            var groupIds = AddOptionGroups(restaurantId, seed);
            AddMenu(restaurantId, seed, groupIds);

            await db.SaveChangesAsync(ct);
        }

        await AddStaffAsync(ct);
    }

    private void AddHours(Guid restaurantId, string slug)
    {
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            // The mezze house closes between lunch and dinner, which is why RestaurantHours
            // allows several rows per day rather than one open/close pair.
            if (slug == "beirut-mezze-house")
            {
                db.RestaurantHours.Add(NewHours(restaurantId, day, 12, 0, 16, 0));
                db.RestaurantHours.Add(NewHours(restaurantId, day, 19, 0, 23, 30));
                continue;
            }

            // The shawarma place never closes. Realistic in Beirut, and deliberately the one
            // restaurant in the seed that is open at every hour: the end-to-end tests place real
            // orders against a real server with no clock they can move, so without a subject
            // like this the whole suite would pass or fail depending on when it happened to run.
            if (slug == "shawarma-station")
            {
                db.RestaurantHours.Add(AllDay(restaurantId, day));
                continue;
            }

            // FriesLab runs past midnight: a close time earlier than the open time means the
            // window crosses into the next day.
            db.RestaurantHours.Add(slug == "frieslab"
                ? NewHours(restaurantId, day, 12, 0, 2, 0)
                : NewHours(restaurantId, day, 10, 0, 23, 0));
        }
    }

    /// <summary>
    /// A day with no closing time. Written as the widest window a <c>TimeOnly</c> holds rather
    /// than as 00:00-23:59, which reads as shut for the last sixty seconds of every day.
    /// </summary>
    private static RestaurantHours AllDay(Guid restaurantId, DayOfWeek day) => new()
    {
        Id = SeedIds.From($"hours:{restaurantId}:{day}:allday"),
        RestaurantId = restaurantId,
        DayOfWeek = day,
        OpenTime = TimeOnly.MinValue,
        CloseTime = TimeOnly.MaxValue,
    };

    private static RestaurantHours NewHours(
        Guid restaurantId, DayOfWeek day, int openHour, int openMinute, int closeHour, int closeMinute) =>
        new()
        {
            Id = SeedIds.From($"hours:{restaurantId}:{day}:{openHour}"),
            RestaurantId = restaurantId,
            DayOfWeek = day,
            OpenTime = new TimeOnly(openHour, openMinute),
            CloseTime = new TimeOnly(closeHour, closeMinute),
        };

    private void AddZones(Guid restaurantId, MenuCatalog.RestaurantSeed seed)
    {
        foreach (var (zone, fee, minutes) in seed.Delivers)
        {
            db.RestaurantZones.Add(new RestaurantZone
            {
                RestaurantId = restaurantId,
                ZoneId = SeedIds.From($"zone:{zone}"),
                DeliveryFeeUsd = fee,
                EstimatedMinutes = minutes,
                IsActive = true,
            });
        }
    }

    private Dictionary<string, Guid> AddOptionGroups(Guid restaurantId, MenuCatalog.RestaurantSeed seed)
    {
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var order = 0;

        foreach (var group in seed.Groups)
        {
            var groupId = SeedIds.From($"group:{seed.Slug}:{group.Name}");
            ids[group.Name] = groupId;

            db.OptionGroups.Add(new OptionGroup
            {
                Id = groupId,
                RestaurantId = restaurantId,
                Name = group.Name,
                MinSelect = group.MinSelect,
                MaxSelect = group.MaxSelect,
                SortOrder = order++,
            });

            var optionOrder = 0;
            foreach (var option in group.Options)
            {
                db.Options.Add(new Option
                {
                    Id = SeedIds.From($"option:{seed.Slug}:{group.Name}:{option.Name}"),
                    OptionGroupId = groupId,
                    Name = option.Name,
                    PriceDeltaUsd = option.PriceDelta,
                    MaxQuantity = option.MaxQuantity,
                    IsAvailable = true,
                    SortOrder = optionOrder++,
                });
            }
        }

        return ids;
    }

    private void AddMenu(
        Guid restaurantId, MenuCatalog.RestaurantSeed seed, Dictionary<string, Guid> groupIds)
    {
        var categoryOrder = 0;

        foreach (var category in seed.Categories)
        {
            var categoryId = SeedIds.From($"category:{seed.Slug}:{category.Name}");
            db.Categories.Add(new Category
            {
                Id = categoryId,
                RestaurantId = restaurantId,
                Name = category.Name,
                SortOrder = categoryOrder++,
                IsActive = true,
            });

            var itemOrder = 0;
            foreach (var item in category.Items)
            {
                var itemId = SeedIds.From($"item:{seed.Slug}:{item.Name}");
                db.MenuItems.Add(new MenuItem
                {
                    Id = itemId,
                    RestaurantId = restaurantId,
                    CategoryId = categoryId,
                    Name = item.Name,
                    Description = item.Description,
                    BasePriceUsd = item.Price,
                    IsAvailable = true,
                    SortOrder = itemOrder++,
                    CreatedAt = DateTimeOffset.UtcNow,
                });

                for (var i = 0; i < item.Groups.Count; i++)
                {
                    var isLast = i == item.Groups.Count - 1;
                    db.MenuItemOptionGroups.Add(new MenuItemOptionGroup
                    {
                        MenuItemId = itemId,
                        OptionGroupId = groupIds[item.Groups[i]],
                        SortOrder = i,
                        MaxSelectOverride = isLast ? item.MaxSelectOverrideOnLastGroup : null,
                    });
                }
            }
        }
    }

    private async Task AddStaffAsync(CancellationToken ct)
    {
        await LinkStaffAsync("owner@frieslab.test", "frieslab", StaffRoleType.Owner, ct);
        await LinkStaffAsync("staff@frieslab.test", "frieslab", StaffRoleType.Staff, ct);
        await LinkStaffAsync("owner@mezze.test", "beirut-mezze-house", StaffRoleType.Owner, ct);
        await LinkStaffAsync("owner@shawarma.test", "shawarma-station", StaffRoleType.Owner, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task LinkStaffAsync(string email, string slug, StaffRoleType role, CancellationToken ct)
    {
        var userId = SeedIds.From($"user:{email}");
        var restaurantId = SeedIds.From($"restaurant:{slug}");

        // IgnoreQueryFilters for the same reason as addresses: RestaurantStaff is tenant-filtered
        // and the seeder has no tenant.
        if (await db.RestaurantStaff.IgnoreQueryFilters()
                .AnyAsync(s => s.UserId == userId && s.RestaurantId == restaurantId, ct))
        {
            return;
        }

        db.RestaurantStaff.Add(new RestaurantStaff
        {
            UserId = userId,
            RestaurantId = restaurantId,
            StaffRole = role,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static async Task AddIfMissingAsync<TEntity>(
        DbSet<TEntity> set, object key, Func<TEntity> create, CancellationToken ct)
        where TEntity : class
    {
        if (await set.FindAsync([key], ct) is null)
        {
            set.Add(create());
        }
    }
}
