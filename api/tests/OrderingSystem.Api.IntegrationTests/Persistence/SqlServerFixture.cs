using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace OrderingSystem.Api.IntegrationTests.Persistence;

/// <summary>
/// Starts a real SQL Server in a container once per test class and applies the migrations to it.
/// <para>
/// A real database is the point. The properties these tests assert - unique indexes, check
/// constraints, decimal precision - are enforced by SQL Server and by nothing else, so the EF
/// in-memory provider would pass them whether the configuration were right or wrong.
/// </para>
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    // The image is pinned explicitly rather than left to the builder's default: it keeps CI
    // reproducible, and it comes from Microsoft's registry rather than Docker Hub.
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// A context scoped to whichever caller is asking. Passing the tenant in is what lets a test
    /// prove that restaurant A cannot see restaurant B's rows.
    /// </summary>
    public AppDbContext CreateContext(ITenantContext? tenant = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new AppDbContext(options, tenant ?? TestTenant.Anonymous);
    }
}

/// <summary>A tenant context a test can shape freely, standing in for a decoded JWT.</summary>
public sealed record TestTenant(Guid? UserId, Guid? RestaurantId, bool IsPlatformAdmin) : ITenantContext
{
    public static readonly TestTenant Anonymous = new(null, null, false);

    public static TestTenant PlatformAdmin() => new(Guid.NewGuid(), null, true);
    public static TestTenant Customer(Guid userId) => new(userId, null, false);
    public static TestTenant Staff(Guid userId, Guid restaurantId) => new(userId, restaurantId, false);
}
