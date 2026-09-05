using System.Reflection;
using OrderingSystem.Application.Features.Catalog;

namespace OrderingSystem.Api.IntegrationTests.Startup;

/// <summary>
/// No two contract types may share a simple name.
///
/// <para>
/// C# namespaces keep <c>Catalog.OpeningWindow</c> and <c>Restaurants.OpeningWindow</c> apart.
/// OpenAPI schema ids do not: both become <c>OpeningWindow</c>, one silently wins, and the
/// generated client then describes the loser with the winner's fields. That shipped — a
/// restaurant's public opening hours were documented as carrying <c>day</c> when the API sends
/// <c>dayOfWeek</c>, and nothing noticed because no client had read them.
/// </para>
/// <para>
/// The contract-drift job in CI cannot catch this. It regenerates the client from the document
/// and compares — and the document is already wrong, consistently, every time.
/// </para>
/// </summary>
public class ContractNameCollisionTests
{
    [Fact]
    public void No_two_contract_types_share_a_name()
    {
        var byName = typeof(RestaurantSummary).Assembly
            .GetExportedTypes()
            .Where(IsContract)
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(" and ", g.Select(t => t.FullName))}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        byName.ShouldBeEmpty(
            "OpenAPI schema ids are a flat namespace, so two contract types with one name become "
            + "one schema and the generated client describes both with whichever shape won. "
            + "Rename one. Found: " + string.Join("; ", byName));
    }

    /// <summary>
    /// What reaches the document: the request and response records under Features.
    ///
    /// <para>
    /// Deliberately not every exported type. Services, validators and abstractions never appear
    /// in a schema, and holding them to a rule about schema names would refuse a perfectly good
    /// name for a reason that does not apply to them.
    /// </para>
    /// </summary>
    private static bool IsContract(Type type) =>
        type is { IsClass: true, IsAbstract: false, IsNested: false }
        && type.Namespace?.Contains(".Features.", StringComparison.Ordinal) == true
        && type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public) is not null;
}
