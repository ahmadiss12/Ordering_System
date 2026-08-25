using System.Runtime.CompilerServices;
using System.Text.Json;

namespace OrderingSystem.Domain.Tests.Architecture;

/// <summary>
/// The repository-level configuration that steps 0 and 1 established. None of it is C#, so the
/// compiler cannot protect it — but several files quietly depend on these values agreeing.
/// <para>
/// The concrete failure this prevents: renaming a container in docker-compose.yml breaks
/// verify.ps1 and the getting-started instructions, and nothing tells you until someone follows
/// the README and it does not work.
/// </para>
/// </summary>
public class RepositoryConventionTests
{
    [Fact]
    public void The_sdk_version_is_pinned_so_machines_cannot_drift()
    {
        using var globalJson = JsonDocument.Parse(File.ReadAllText(RepoFile("global.json")));

        var version = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString();

        version.ShouldNotBeNull();
        version.ShouldStartWith("10.", Case.Sensitive, "the solution targets net10.0");
    }

    [Fact]
    public void Compose_declares_the_containers_the_scripts_and_docs_expect()
    {
        var compose = File.ReadAllText(RepoFile(Path.Combine("docker", "docker-compose.yml")));

        // verify.ps1 checks for these names, and the README tells people to look for them.
        foreach (var name in new[] { "ordering-sqlserver", "ordering-mailpit" })
        {
            compose.ShouldContain(name, Case.Sensitive,
                $"verify.ps1 looks for a container called {name}");
        }

        compose.ShouldContain("1433", Case.Sensitive, "the connection strings assume this port");
        compose.ShouldContain("8025", Case.Sensitive, "the README sends people to Mailpit on this port");
    }

    [Fact]
    public void Package_versions_are_managed_centrally()
    {
        var props = File.ReadAllText(RepoFile(Path.Combine("api", "Directory.Packages.props")));

        props.ShouldContain("<ManagePackageVersionsCentrally>true", Case.Sensitive);

        // Transitive pinning is what let a vulnerable package arriving through a dependency be
        // forced forward without waiting for that dependency to update.
        props.ShouldContain("<CentralPackageTransitivePinningEnabled>true", Case.Sensitive);
    }

    [Fact]
    public void Warnings_are_errors_across_every_project()
    {
        var props = File.ReadAllText(RepoFile(Path.Combine("api", "Directory.Build.props")));

        // This setting is what caught a high-severity advisory in a transitive package before
        // any application code existed. Turning it off would silence that class of finding.
        props.ShouldContain("<TreatWarningsAsErrors>true", Case.Sensitive);
        props.ShouldContain("<Nullable>enable", Case.Sensitive);
    }

    [Fact]
    public void Secrets_are_not_committed()
    {
        var gitignore = File.ReadAllText(RepoFile(".gitignore"));
        gitignore.ShouldContain(".env", Case.Sensitive);

        // The shipped appsettings.json must never carry a usable signing key; the real one comes
        // from configuration per environment.
        var appsettings = File.ReadAllText(RepoFile(
            Path.Combine("api", "src", "OrderingSystem.Api", "appsettings.json")));

        using var document = JsonDocument.Parse(appsettings);
        document.RootElement.GetProperty("Jwt").GetProperty("SigningKey").GetString()
            .ShouldBeEmpty("a committed signing key is a signing key everyone has");
    }

    private static string RepoFile(string relativePath) =>
        Path.Combine(RepositoryRoot(), relativePath);

    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Directory.GetParent(thisFile)!   // Architecture/
            .Parent!                     // OrderingSystem.Domain.Tests/
            .Parent!                     // tests/
            .Parent!                     // api/
            .Parent!                     // repository root
            .FullName;
}
