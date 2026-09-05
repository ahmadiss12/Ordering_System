using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Infrastructure.Persistence;
using OrderingSystem.Infrastructure.Persistence.Seed;

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

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();

        // Seed once, so catalog tests have three real restaurants with real menus to read rather
        // than each building its own fixture. Auth tests are unaffected: they register throwaway
        // accounts with random addresses.
        await using var db = _database.CreateContext(TestTenant.PlatformAdmin());
        await new DatabaseSeeder(db, NullLogger<DatabaseSeeder>.Instance).SeedAsync();
    }

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
                // Two different origins on purpose, so a test can tell which link a mail carried:
                // a customer's reset goes to the storefront and a staff invitation to the
                // dashboard, and a single value would let them be swapped without noticing.
                ["Auth:AppBaseUrl"] = "http://storefront.test",
                ["Auth:DashboardBaseUrl"] = "http://dashboard.test",
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

            // Opening hours are wall-clock, so without this every test that places an order
            // fails for the ten hours a day the seeded restaurant is shut.
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }

    public AppDbContext CreateDbContext(ITenantContext? tenant = null) => _database.CreateContext(tenant);

    /// <summary>The clock the server runs on. Move its local time to test opening hours.</summary>
    public TestClock Clock { get; } = new();

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
    private bool _failNext;

    public IReadOnlyList<(string To, string Subject, string Body)> Sent
    {
        get { lock (_sent) { return _sent.ToArray(); } }
    }

    /// <summary>
    /// Makes the next send throw, once. A mail server that is briefly unreachable is an ordinary
    /// event, and what a caller does about it is worth being able to test — the real sender
    /// throws a MailKit exception straight out of ConnectAsync when nothing is listening.
    /// </summary>
    public void FailNextSend()
    {
        lock (_sent) { _failNext = true; }
    }

    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        lock (_sent)
        {
            if (_failNext)
            {
                _failNext = false;
                throw new InvalidOperationException("The mail server is not answering.");
            }

            _sent.Add((toEmail, subject, body));
        }

        return Task.CompletedTask;
    }
}
