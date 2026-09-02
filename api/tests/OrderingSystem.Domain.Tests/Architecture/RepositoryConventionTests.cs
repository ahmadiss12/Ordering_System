using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrderingSystem.Domain.Enums;

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

    [Fact]
    public void Line_endings_are_decided_by_the_repository_not_the_machine()
    {
        // .editorconfig says end_of_line = lf, but that only instructs editors. Without this
        // file, git on Windows checks everything out as CRLF (core.autocrlf defaults to true),
        // and anything that reads a file and matches on line structure behaves differently
        // there than in CI - which is how a role-name test came to fail on every Windows clone
        // while the badge stayed green.
        var attributes = File.ReadAllText(RepoFile(".gitattributes"));

        attributes.ShouldContain("text=auto", Case.Sensitive);
        attributes.ShouldContain("eol=lf", Case.Sensitive,
            "line endings must be normalised by git, not left to each machine's default");
    }

    [Fact]
    public void Uploaded_photos_are_not_committed()
    {
        // ImageStorageOptions.RootPath points inside the working tree, so every photo a developer
        // uploads while running the API locally appears as an untracked file. Without this line
        // in .gitignore they get swept into a commit by `git add -A`, which is how one machine's
        // test uploads end up in everybody's clone.
        var gitignore = File.ReadAllText(RepoFile(".gitignore"));

        gitignore.ShouldContain("wwwroot/media", Case.Sensitive,
            "uploaded images are runtime data, not source");
    }

    [Fact]
    public void The_web_role_names_match_the_RoleType_enum()
    {
        // The token issuer serialises roles with nameof(RoleType.X), and the browser reads those
        // strings to decide which navigation to draw. Nothing in the OpenAPI document carries the
        // enum, so the contract job cannot catch a rename here — this test is the only thing that
        // does. Without it, renaming a member leaves the dashboard silently drawing an empty menu
        // for every owner.
        // Normalised on read. The regex below anchors with "$", which in .NET matches only
        // immediately before "\n" — so on a CRLF checkout the "\r" sits between the comma and
        // the anchor and nothing matches. That failed on every Windows clone while CI, which
        // checks out LF, stayed green. .gitattributes now stops the CRLF arriving at all; this
        // keeps the test from caring either way, because it is asserting about role names and
        // not about line endings.
        var rolesTs = File.ReadAllText(RepoFile(Path.Combine(
            "web", "projects", "shared", "auth", "src", "lib", "roles.ts")))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        foreach (var name in Enum.GetNames<RoleType>())
        {
            rolesTs.ShouldContain($"{name}: '{name}'", Case.Sensitive,
                $"web/.../auth/src/lib/roles.ts must list {name}; update it to match RoleType");
        }

        // The reverse direction: a name in roles.ts that no longer exists in the enum would let a
        // guard ask for a role the server can never issue, locking people out of a working page.
        var declared = Regex.Matches(rolesTs, @"^\s{2}(\w+): '(\w+)',$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToArray();

        declared.ShouldBe(Enum.GetNames<RoleType>(), ignoreOrder: true,
            "roles.ts and RoleType must describe exactly the same set of roles");
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
