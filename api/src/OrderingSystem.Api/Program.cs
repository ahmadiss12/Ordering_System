using OrderingSystem.Api.Auth;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Infrastructure;
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
