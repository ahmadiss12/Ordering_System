using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Geography;

namespace OrderingSystem.Application.Features.Restaurants;

/// <summary>
/// Where a restaurant delivers, what it charges, and how long it says the drive takes.
///
/// <para>
/// The zones themselves belong to the platform. A restaurant picks from them rather than inventing
/// its own Hamra — which is what makes a customer's saved address and a restaurant's coverage
/// comparable at all, and is the reason ADR-13 chose zones over distances in a country where
/// street addresses do not reliably geocode.
/// </para>
/// </summary>
public sealed class RestaurantZonesService(
    IAppDbContext db, ITenantGuard guard, IValidationService validation)
{
    public async Task<IReadOnlyList<RestaurantZoneResponse>> ListAsync(CancellationToken ct = default)
    {
        var restaurantId = guard.RequireRestaurantId();

        // Every active zone, served or not. A screen showing only the configured ones would make
        // adding the first zone impossible to find.
        return await db.DeliveryZones.AsNoTracking()
            .Where(z => z.IsActive)
            .OrderBy(z => z.Name)
            .Select(z => new RestaurantZoneResponse(
                z.Id,
                z.Name,
                z.RestaurantZones.Any(r => r.RestaurantId == restaurantId && r.IsActive),
                z.RestaurantZones
                    .Where(r => r.RestaurantId == restaurantId)
                    .Select(r => (decimal?)r.DeliveryFeeUsd)
                    .FirstOrDefault(),
                z.RestaurantZones
                    .Where(r => r.RestaurantId == restaurantId)
                    .Select(r => (int?)r.EstimatedMinutes)
                    .FirstOrDefault()))
            .ToListAsync(ct);
    }

    public async Task<RestaurantZoneResponse> SetAsync(
        Guid zoneId, SetRestaurantZoneRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        var restaurantId = guard.RequireRestaurantId();

        var zone = await db.DeliveryZones.AsNoTracking()
            .Where(z => z.Id == zoneId)
            .Select(z => new { z.Id, z.Name, z.IsActive })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("That delivery zone does not exist.");

        if (!zone.IsActive)
        {
            // The platform has withdrawn the zone. A restaurant agreeing to deliver there would
            // be promising something no customer can order anyway, since addresses in a withdrawn
            // zone are not offered either.
            throw new ConflictException($"{zone.Name} is not a zone the platform is using.");
        }

        var existing = await db.RestaurantZones
            .FirstOrDefaultAsync(r => r.RestaurantId == restaurantId && r.ZoneId == zoneId, ct);

        if (existing is null)
        {
            db.RestaurantZones.Add(new RestaurantZone
            {
                RestaurantId = restaurantId,
                ZoneId = zoneId,
                DeliveryFeeUsd = request.DeliveryFeeUsd,
                EstimatedMinutes = request.EstimatedMinutes,
                IsActive = request.IsServed,
            });
        }
        else
        {
            // Updated rather than deleted when a zone is switched off, which is what the IsActive
            // column is for: a restaurant suspending Jounieh for a fortnight keeps its fee and its
            // travel time, and turning it back on is one press instead of a re-entry.
            existing.DeliveryFeeUsd = request.DeliveryFeeUsd;
            existing.EstimatedMinutes = request.EstimatedMinutes;
            existing.IsActive = request.IsServed;
        }

        await db.SaveChangesAsync(ct);

        return new RestaurantZoneResponse(
            zone.Id, zone.Name, request.IsServed, request.DeliveryFeeUsd, request.EstimatedMinutes);
    }
}
