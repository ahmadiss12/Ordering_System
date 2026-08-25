using System.Runtime.CompilerServices;

namespace OrderingSystem.Domain.Tests.Architecture;

/// <summary>
/// IgnoreQueryFilters switches off tenant isolation for a query. It has legitimate uses, but each
/// one is a hole in the boundary described in ADR-07, so the set of holes is pinned here rather
/// than left to grow quietly.
/// <para>
/// The realistic failure is not malice — it is someone debugging "why does this return nothing?",
/// finding that removing the filter fixes it, and shipping that.
/// </para>
/// </summary>
public class QueryFilterBypassTests
{
    /// <summary>
    /// Files permitted to bypass filters, and why. Adding to this list should require explaining
    /// the entry in review; that is the entire point of the list existing.
    /// </summary>
    private static readonly (string File, string Reason)[] Allowed =
    [
        ("DatabaseSeeder.cs",
         "Runs from the CLI with no signed-in user, so its own existence checks would be filtered out."),

        ("AuthService.cs",
         "Reads RestaurantStaff to decide what the tenant IS. Filtering it would ask a question "
         + "that can only be answered after it has been answered."),
    ];

    [Fact]
    public void Only_allowlisted_files_bypass_the_tenant_query_filters()
    {
        var offenders = SourceFiles()
            .Where(file => File.ReadAllText(file).Contains("IgnoreQueryFilters", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(name => !Allowed.Any(a => string.Equals(a.File, name, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty(
            "IgnoreQueryFilters disables tenant isolation for that query. If the bypass is genuinely "
            + "needed, add the file to the allowlist in this test with a reason. Found: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void The_allowlist_contains_no_stale_entries()
    {
        // An allowlist that outlives its reason is worse than none: it silently re-permits the
        // bypass if that filename is ever reused for something else.
        var actuallyBypassing = SourceFiles()
            .Where(file => File.ReadAllText(file).Contains("IgnoreQueryFilters", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);

        var stale = Allowed
            .Where(a => !actuallyBypassing.Contains(a.File))
            .Select(a => a.File)
            .ToArray();

        stale.ShouldBeEmpty(
            "These files no longer bypass query filters, so their allowlist entries should go: "
            + string.Join(", ", stale));
    }

    private static IEnumerable<string> SourceFiles()
    {
        var src = Path.Combine(RepositoryApiDirectory(), "src");

        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string RepositoryApiDirectory([CallerFilePath] string thisFile = "") =>
        Directory.GetParent(thisFile)!   // Architecture/
            .Parent!                     // OrderingSystem.Domain.Tests/
            .Parent!                     // tests/
            .Parent!                     // api/
            .FullName;
}
