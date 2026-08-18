using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace OrderingSystem.Domain.Tests.Architecture;

/// <summary>
/// ADR-02 states that Domain depends on nothing. A comment saying so is a wish;
/// this makes it a build failure. It reads the project file rather than the compiled
/// assembly so that it holds even while Domain is still empty.
/// </summary>
public class DependencyRuleTests
{
    [Fact]
    public void Domain_declares_no_package_or_project_references()
    {
        var project = XDocument.Load(DomainProjectPath());

        var references = project.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? "(unnamed)")
            .ToArray();

        references.ShouldBeEmpty(
            "Domain holds entities, the order transition table and the money rules. "
            + "It must not know about EF Core, ASP.NET or any I/O (ADR-02). "
            + "Found: " + string.Join(", ", references));
    }

    // Resolved from this file's compile-time location, so it survives being run
    // from any working directory. Assumes build and test happen on one machine,
    // which is true locally and in CI.
    private static string DomainProjectPath([CallerFilePath] string thisFile = "")
    {
        var apiDirectory = Directory.GetParent(thisFile)!  // Architecture/
            .Parent!                                       // OrderingSystem.Domain.Tests/
            .Parent!                                       // tests/
            .Parent!;                                      // api/

        return Path.Combine(
            apiDirectory.FullName, "src", "OrderingSystem.Domain", "OrderingSystem.Domain.csproj");
    }
}
