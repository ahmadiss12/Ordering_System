using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderingSystem.Infrastructure.Persistence.Design;

/// <summary>
/// Used only by "dotnet ef" at design time. The tooling cannot resolve AppDbContext from DI
/// because its constructor needs a request-scoped tenant, so it builds one here instead.
/// <para>
/// The connection string is never opened to scaffold a migration - EF needs a provider to know
/// which SQL dialect to emit, not a live database. Applying a migration uses the running
/// application's configuration.
/// </para>
/// </summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ORDERING_CONNECTION")
            ?? "Server=localhost,1433;Database=OrderingSystem;User Id=sa;"
             + "Password=Your_Strong_Passw0rd;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(
                typeof(AppDbContextFactory).Assembly.FullName))
            .Options;

        return new AppDbContext(options, new DesignTimeTenantContext());
    }
}
