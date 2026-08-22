using OrderingSystem.Api.Auth;
using OrderingSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using OrderingSystem.Infrastructure;
using OrderingSystem.Infrastructure.Persistence;
using OrderingSystem.Infrastructure.Persistence.Seed;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddHttpContextAccessor();

// Scoped, not singleton: it reads the current request's claims, and a singleton would leak one
// caller's identity into another's queries.
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// "dotnet run -- --seed" applies migrations, fills the database with demo data, and exits.
// Deliberately behind a flag rather than on startup: a seeder that runs itself eventually runs
// somewhere it should not.
if (args.Contains("--seed", StringComparer.Ordinal))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.MapControllers();

// Liveness probe. Kept trivial on purpose: it must not touch the database,
// or a slow query will report the whole API as down.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

await app.RunAsync();

// Exposed so the integration tests can drive the real pipeline through
// WebApplicationFactory<Program>, which needs a nameable entry-point type.
public partial class Program;
