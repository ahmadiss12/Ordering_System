using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OrderingSystem.Api.Auth;
using OrderingSystem.Api.Middleware;
using OrderingSystem.Api.OpenApi;
using OrderingSystem.Application;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Application.Features.Auth;
using OrderingSystem.Infrastructure;
using OrderingSystem.Infrastructure.Persistence;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using OrderingSystem.Infrastructure.Persistence.Seed;
using OrderingSystem.Infrastructure.Storage;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi(options => options.AddOperationTransformer<OperationIdTransformer>());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddHttpContextAccessor();

// Scoped, not singleton: it reads the current request's claims, and a singleton would leak one
// caller's identity into another's queries.
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, ASP.NET rewrites "sub" into a long legacy URI claim type and the token's
        // real shape stops matching what the code reads. Off means claims arrive as issued.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSection["SigningKey"] ?? string.Empty)),

            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = HttpTenantContext.RoleClaim,

            // The default five minutes means an expired token keeps working for five more.
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization(options => options.AddOrderingPolicies());

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

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

// Serve uploaded images from wherever the storage option points, rather than from wwwroot.
// Scoped to that one directory: a static-file handler rooted any higher would serve appsettings.
var imageOptions = app.Services.GetRequiredService<IOptions<ImageStorageOptions>>().Value;
var imageRoot = Path.GetFullPath(imageOptions.RootPath);
Directory.CreateDirectory(imageRoot);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imageRoot),
    RequestPath = imageOptions.PublicPath.TrimEnd('/'),
    ServeUnknownFileTypes = false,
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Liveness probe. Kept trivial on purpose: it must not touch the database,
// or a slow query will report the whole API as down.
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("Health");

await app.RunAsync();

// Exposed so the integration tests can drive the real pipeline through
// WebApplicationFactory<Program>, which needs a nameable entry-point type.
public partial class Program;
