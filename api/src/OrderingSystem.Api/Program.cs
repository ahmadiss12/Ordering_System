using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

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
