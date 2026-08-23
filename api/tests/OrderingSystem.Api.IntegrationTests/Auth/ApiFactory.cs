using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Infrastructure.Persistence;

namespace OrderingSystem.Api.IntegrationTests.Auth;

/// <summary>
/// Runs the real API pipeline in-process against a real SQL Server, with only two things
/// substituted: the connection string, and the email sender. Everything the tests assert —
/// routing, model binding, validation, the exception handler, EF, the query filters — is the
/// production wiring rather than a stand-in.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqlServerFixture _database = new();

    public CapturedEmails Emails { get; } = new();

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _database.ConnectionString,
                ["Jwt:SigningKey"] = "integration-test-signing-key-at-least-32-chars",
                ["Auth:AppBaseUrl"] = "http://localhost:4200",
            }));

        builder.ConfigureTestServices(services =>
        {
            // The only real substitution. Sending mail is not what these tests are about, and
            // capturing it is how a test reads the reset link a user would have clicked.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    public AppDbContext CreateDbContext(ITenantContext? tenant = null) => _database.CreateContext(tenant);
}

/// <summary>Collects what would have been emailed, so a test can pull the token out of the body.</summary>
public sealed class CapturedEmails : IEmailSender
{
    private readonly List<(string To, string Subject, string Body)> _sent = [];

    public IReadOnlyList<(string To, string Subject, string Body)> Sent
    {
        get { lock (_sent) { return _sent.ToArray(); } }
    }

    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        lock (_sent) { _sent.Add((toEmail, subject, body)); }
        return Task.CompletedTask;
    }
}
