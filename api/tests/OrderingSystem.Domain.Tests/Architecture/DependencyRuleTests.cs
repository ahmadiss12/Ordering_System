using System.Reflection;
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

    [Fact]
    public void Domain_assembly_references_nothing_outside_the_base_class_library()
    {
        // The project-file check above catches a declared reference. This catches one that
        // arrives some other way - a shared props file, a transitive pin, a stray using.
        var forbiddenPrefixes = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Microsoft.Extensions",
            "Newtonsoft",
            "FluentValidation",
            "Riok.Mapperly",
            "Serilog",
        };

        // Fully qualified: relative to this namespace, "Orders" would resolve to the test
        // project's own Orders folder once one exists, and typeof would stop naming the
        // assembly this rule is actually about.
        var offenders = typeof(OrderingSystem.Domain.Orders.Order).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => forbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .ToArray();

        offenders.ShouldBeEmpty(
            "Domain must depend on the base class library alone (ADR-02). Found: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_domain_enum_gives_its_members_explicit_values()
    {
        // Enum members are stored as integers. If someone inserts a value in the middle and the
        // numbers shift, every existing row silently changes meaning. Explicit values make that
        // impossible, so this asserts nobody ever relies on positional defaults.
        var implicitlyNumbered = typeof(Enums.OrderStatus).Assembly
            .GetTypes()
            .Where(t => t.IsEnum && t.Namespace == "OrderingSystem.Domain.Enums")
            .Where(t => Enum.GetValues(t).Cast<object>()
                .Select(v => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture))
                .Contains(0))
            .Select(t => t.Name)
            .ToArray();

        implicitlyNumbered.ShouldBeEmpty(
            "A domain enum has a member valued 0, which is what C# assigns when values are left "
            + "implicit. Number every member explicitly, starting at 1. Found: "
            + string.Join(", ", implicitlyNumbered));
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
