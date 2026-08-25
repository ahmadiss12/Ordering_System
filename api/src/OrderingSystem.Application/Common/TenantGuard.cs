using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Exceptions;

namespace OrderingSystem.Application.Common;

public sealed class TenantGuard(ITenantContext tenant) : ITenantGuard
{
    public void EnsureCanActFor(Guid restaurantId)
    {
        if (tenant.IsPlatformAdmin)
        {
            return;
        }

        if (tenant.RestaurantId is null)
        {
            throw new ForbiddenException("This action requires a restaurant staff account.");
        }

        if (tenant.RestaurantId != restaurantId)
        {
            // 403 rather than 404, per spec §4. It does confirm the resource exists, which a 404
            // would not - the spec chose the clearer error over that small disclosure.
            throw new ForbiddenException("This resource belongs to another restaurant.");
        }
    }

    public Guid RequireRestaurantId() =>
        tenant.RestaurantId
        ?? throw new ForbiddenException("This action requires a restaurant staff account.");

    public Guid RequireUserId() =>
        tenant.UserId
        ?? throw new AuthenticationFailedException("This action requires you to be signed in.");
}
