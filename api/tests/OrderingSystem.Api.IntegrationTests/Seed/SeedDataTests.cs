using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Infrastructure.Persistence;
using OrderingSystem.Infrastructure.Persistence.Seed;

namespace OrderingSystem.Api.IntegrationTests.Seed;

/// <summary>
/// A database with the seeder already run twice — because "safe to run twice" is the property
/// most worth proving, and the only way to prove it is to run it twice.
/// </summary>
public sealed class SeededDatabase : IAsyncLifetime
{
    private readonly SqlServerFixture _database = new();

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        await SeedAsync();
        FirstRunCounts = await CountsAsync();
        await SeedAsync();
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    public IReadOnlyDictionary<string, int> FirstRunCounts { get; private set; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public AppDbContext Context() => _database.CreateContext(TestTenant.PlatformAdmin());

    public async Task<IReadOnlyDictionary<string, int>> CountsAsync()
    {
        await using var db = Context();
        return new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["restaurants"] = await db.Restaurants.CountAsync(),
            ["categories"] = await db.Categories.CountAsync(),
            ["menuItems"] = await db.MenuItems.CountAsync(),
            ["optionGroups"] = await db.OptionGroups.CountAsync(),
            ["options"] = await db.Options.CountAsync(),
            ["itemGroupLinks"] = await db.MenuItemOptionGroups.CountAsync(),
            ["zones"] = await db.DeliveryZones.CountAsync(),
            ["restaurantZones"] = await db.RestaurantZones.CountAsync(),
            ["users"] = await db.Users.CountAsync(),
            ["hours"] = await db.RestaurantHours.CountAsync(),
        };
    }

    private async Task SeedAsync()
    {
        await using var db = _database.CreateContext(TestTenant.PlatformAdmin());
        await new DatabaseSeeder(db, NullLogger<DatabaseSeeder>.Instance).SeedAsync();
    }
}

public sealed class SeedDataTests(SeededDatabase seeded) : IClassFixture<SeededDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Running_the_seeder_twice_changes_nothing()
    {
        // The property that makes the seeder safe to put in verify.ps1 and in anyone's
        // "reset my database" habit.
        var afterSecondRun = await seeded.CountsAsync();

        afterSecondRun.ShouldBe(seeded.FirstRunCounts);
    }

    [Fact]
    public async Task Every_listed_restaurant_has_a_menu_and_a_delivery_area()
    {
        await using var db = seeded.Context();

        var restaurants = await db.Restaurants
            .Where(r => r.IsActive)
            .Select(r => new
            {
                r.Name,
                Items = r.MenuItems.Count,
                Zones = r.Zones.Count,
                Hours = r.Hours.Count,
            })
            .ToListAsync(Ct);

        restaurants.Count.ShouldBe(3);
        restaurants.ShouldAllBe(r => r.Items > 0);
        restaurants.ShouldAllBe(r => r.Zones > 0);
        restaurants.ShouldAllBe(r => r.Hours > 0);
    }

    [Fact]
    public async Task One_restaurant_is_seeded_with_nothing_set_up()
    {
        await using var db = seeded.Context();

        var fresh = await db.Restaurants
            .Where(r => r.Slug == DatabaseSeeder.UnconfiguredSlug)
            .Select(r => new
            {
                r.IsActive,
                Items = r.MenuItems.Count,
                Zones = r.Zones.Count,
                Hours = r.Hours.Count,
                Owners = r.Staff.Count,
            })
            .FirstOrDefaultAsync(Ct);

        // Deliberately empty, and the onboarding journey depends on it staying that way: it signs
        // in as this owner and takes the restaurant from here to taking orders. A well-meaning
        // addition of hours or a menu here would leave that test proving nothing.
        fresh.ShouldNotBeNull();
        fresh.IsActive.ShouldBeFalse("nobody has listed it yet");
        fresh.Items.ShouldBe(0);
        fresh.Zones.ShouldBe(0);
        fresh.Hours.ShouldBe(0);

        // With an owner, though. Somebody has to be able to sign in and do the setting up.
        fresh.Owners.ShouldBe(1);
    }

    [Fact]
    public async Task The_seeded_menus_cover_every_option_shape_the_model_supports()
    {
        await using var db = seeded.Context();
        var groups = await db.OptionGroups.Select(g => new { g.MinSelect, g.MaxSelect }).ToListAsync(Ct);

        // If the seed data only ever exercised one shape, the model's flexibility would be
        // untested by anything a person actually clicks through.
        groups.ShouldContain(g => g.MinSelect == 1 && g.MaxSelect == 1, "a required radio group");
        groups.ShouldContain(g => g.MinSelect == 0 && g.MaxSelect == null, "unlimited checkboxes");
        groups.ShouldContain(g => g.MinSelect == 0 && g.MaxSelect > 1, "a capped group");

        var options = await db.Options.Select(o => o.PriceDeltaUsd).ToListAsync(Ct);
        options.ShouldContain(0m, "zero-cost options such as 'no pickles'");
        options.ShouldContain(d => d > 0m);

        (await db.Options.AnyAsync(o => o.MaxQuantity > 1, Ct))
            .ShouldBeTrue("an option that can be taken twice, such as an extra patty");
    }

    [Fact]
    public async Task A_shared_option_group_is_widened_for_one_item_only()
    {
        await using var db = seeded.Context();

        var sauces = await db.MenuItemOptionGroups
            .Where(m => m.OptionGroup.Name == "Sauces")
            .Select(m => new { Item = m.MenuItem.Name, m.MaxSelectOverride, m.OptionGroup.MaxSelect })
            .ToListAsync(Ct);

        // The whole reason the override columns exist: one group, different limits per item.
        sauces.ShouldContain(s => s.MaxSelectOverride == null, "most items inherit the group's cap");
        sauces.ShouldContain(s => s.MaxSelectOverride > s.MaxSelect, "the platter is allowed more");
    }

    [Fact]
    public async Task A_kitchen_that_closes_between_services_has_two_windows_in_one_day()
    {
        await using var db = seeded.Context();

        var mondayWindows = await db.RestaurantHours
            .Where(h => h.Restaurant.Slug == "beirut-mezze-house" && h.DayOfWeek == DayOfWeek.Monday)
            .CountAsync(Ct);

        mondayWindows.ShouldBe(2, "one open/close pair per day could not express a lunch-dinner split");
    }

    [Fact]
    public async Task A_kitchen_open_past_midnight_closes_earlier_than_it_opens()
    {
        await using var db = seeded.Context();

        var overnight = await db.RestaurantHours
            .AnyAsync(h => h.Restaurant.Slug == "frieslab" && h.CloseTime < h.OpenTime, Ct);

        overnight.ShouldBeTrue("a close time before the open time is how a window crossing midnight is stored");
    }

    [Fact]
    public async Task The_same_zone_costs_a_different_amount_from_different_restaurants()
    {
        await using var db = seeded.Context();

        var fees = await db.RestaurantZones
            .Where(z => z.Zone.Name == "Achrafieh")
            .Select(z => z.DeliveryFeeUsd)
            .ToListAsync(Ct);

        fees.Count.ShouldBeGreaterThan(1);
        fees.Distinct().Count().ShouldBeGreaterThan(1,
            "the fee belongs to the restaurant-zone pair, not to the zone");
    }

    [Fact]
    public async Task Seeded_accounts_cover_every_role_and_carry_hashed_passwords()
    {
        await using var db = seeded.Context();

        var roles = await db.UserRoles.Select(r => r.Role).Distinct().ToListAsync(Ct);
        roles.Count.ShouldBeGreaterThanOrEqualTo(4, "a demo needs one account per role to be useful");

        var hashes = await db.Users.Select(u => u.PasswordHash).ToListAsync(Ct);
        hashes.ShouldAllBe(h => h.Length > 20);
        hashes.ShouldNotContain(DatabaseSeeder.SeedPassword, "the seed password must never be stored as written");
    }

    [Fact]
    public async Task Staff_accounts_are_attached_to_a_restaurant()
    {
        await using var db = seeded.Context();

        // Without this row, login cannot put a restaurant_id claim in the token, and the
        // dashboard would have nothing to scope to.
        var memberships = await db.RestaurantStaff.CountAsync(Ct);

        memberships.ShouldBeGreaterThan(0);
    }
}
