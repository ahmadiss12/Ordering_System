using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Infrastructure.Persistence;
using OrderingSystem.Infrastructure.Persistence.Seed;

namespace OrderingSystem.Infrastructure;

/// <summary>
/// The one place the Api project is allowed to reach into Infrastructure. Controllers depend on
/// Application types; only Program.cs calls this.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is missing. Set ConnectionStrings__Default.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);

                // A transient network blip should not surface as a failed checkout.
                sql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null);
            }));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
