using OrderingSystem.Application.Abstractions;

namespace OrderingSystem.Infrastructure.Persistence.Design;

/// <summary>
/// Stands in for the request-scoped tenant when the EF tooling builds the model to scaffold a
/// migration. Nothing is queried at that point, so the values only have to be shaped correctly -
/// but they are deliberately the least privileged combination rather than admin, so that a design
/// -time path can never be mistaken for an authorised one.
/// </summary>
internal sealed class DesignTimeTenantContext : ITenantContext
{
    public Guid? UserId => null;
    public Guid? RestaurantId => null;
    public bool IsPlatformAdmin => false;
}
