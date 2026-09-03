using System.Net;
using System.Text.Json;
using OrderingSystem.Api.IntegrationTests.Auth;
using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Api.IntegrationTests.Startup;

/// <summary>
/// Step 5's claim: the application boots, connects to its database, and serves. Each of these
/// would have caught a real failure during that step — a missing connection string, an
/// unregistered service, a signing key the options validator rejects.
/// </summary>
public sealed class ApiStartupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_application_starts_and_reports_healthy()
    {
        // Reaching this at all means the whole composition root resolved: DbContext, tenant
        // context, auth, validators, the seeder. A missing registration fails here.
        var response = await factory.CreateClient().GetAsync("/health", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("status").GetString().ShouldBe("ok");
    }

    [Fact]
    public async Task The_health_check_answers_without_authentication()
    {
        // A probe that needs a token is useless to a load balancer.
        var response = await factory.CreateClient().GetAsync("/health", Ct);

        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_openapi_document_describes_every_auth_endpoint()
    {
        // ADR-14 makes this document the source of the generated TypeScript client, so an
        // endpoint missing from it is an endpoint no client can call.
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var paths = document.RootElement.GetProperty("paths");

        foreach (var expected in new[]
                 {
                     "/api/auth/register", "/api/auth/login", "/api/auth/refresh",
                     "/api/auth/logout", "/api/auth/forgot-password", "/api/auth/reset-password",
                 })
        {
            paths.TryGetProperty(expected, out _).ShouldBeTrue($"{expected} must appear in the OpenAPI document");
        }
    }

    [Fact]
    public async Task Every_enum_in_the_document_carries_its_names()
    {
        // Without this the document says {"type": "integer"} and nothing more, so the generated
        // TypeScript types every enum as `number` and a screen accepting an order posts { to: 2 }.
        // A magic number is bad on its own; one that means something else after somebody
        // renumbers the C# enum is a bug no build catches.
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        var status = schemas.GetProperty(nameof(OrderStatus));

        status.GetProperty("enum").EnumerateArray().Select(v => v.GetInt32())
            .ShouldBe(Enum.GetValues<OrderStatus>().Select(v => (int)v));

        // x-enumNames is the convention NSwag reads to emit a named TypeScript enum.
        status.GetProperty("x-enumNames").EnumerateArray().Select(v => v.GetString())
            .ShouldBe(Enum.GetNames<OrderStatus>());
    }

    [Fact]
    public async Task No_enum_is_left_as_a_bare_integer()
    {
        // Named on purpose rather than checked one by one: the transformer runs over every enum
        // the document mentions, so one arriving without names means it stopped running rather
        // than that somebody forgot to list it here.
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json", Ct);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        var bare = document.RootElement.GetProperty("components").GetProperty("schemas")
            .EnumerateObject()
            .Where(s => IsDomainEnum(s.Name))
            .Where(s => !s.Value.TryGetProperty("x-enumNames", out _))
            .Select(s => s.Name)
            .ToArray();

        bare.ShouldBeEmpty(
            "these enums reach the client as a bare number: " + string.Join(", ", bare));
    }

    private static bool IsDomainEnum(string schemaName) =>
        typeof(OrderStatus).Assembly.GetType($"OrderingSystem.Domain.Enums.{schemaName}")?.IsEnum == true;

    [Fact]
    public async Task An_unknown_route_returns_404_rather_than_an_error()
    {
        var response = await factory.CreateClient().GetAsync("/api/does-not-exist", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Malformed_json_is_a_400_and_never_leaks_internals()
    {
        using var content = new StringContent("{ this is not json", System.Text.Encoding.UTF8, "application/json");
        var response = await factory.CreateClient().PostAsync("/api/auth/login", content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldNotContain("StackTrace", Case.Insensitive);
        body.ShouldNotContain("Password=", Case.Insensitive, "a connection string must never reach a client");
    }
}
