using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
                ["Jwt:SigningKey"] = "integration-test-signing-key-at-least-32-chars",
                ["Auth:AppBaseUrl"] = "http://localhost:4200",
            }));

        // Without this, a 500 in a test is opaque: the exception handler deliberately hides the
        // detail from the client, so the server-side log is the only place the cause exists.
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new FileLoggerProvider(ServerLogPath));
            logging.SetMinimumLevel(LogLevel.Warning);
        });

        builder.ConfigureTestServices(services =>
        {
            // The DbContext is re-registered outright rather than overridden through
            // configuration.
            //
            // Configuration was the original approach and it silently lost: appsettings.
            // Development.json also defines ConnectionStrings:Default, it won, and these tests
            // ran against the developer's local database instead of the container. They passed -
            // because that database happened to exist - which is the worst possible failure mode
            // for a test. Replacing the registration leaves nothing for precedence to decide.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(_database.ConnectionString));

            // Sending mail is not what these tests are about, and capturing it is how a test
            // reads the reset link a user would have clicked.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    public AppDbContext CreateDbContext(ITenantContext? tenant = null) => _database.CreateContext(tenant);

    /// <summary>Where the in-process host writes warnings and errors during a test run.</summary>
    public static string ServerLogPath { get; } =
        Path.Combine(Path.GetTempPath(), "ordering-system-test-server.log");
}

internal sealed class FileLoggerProvider(string path) : ILoggerProvider
{
    private static readonly Lock Gate = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(path, categoryName);

    public void Dispose() { }

    private sealed class FileLogger(string path, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var line = $"[{logLevel}] {category}: {formatter(state, exception)}\n{exception}\n";
            lock (Gate) { File.AppendAllText(path, line); }
        }
    }
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
