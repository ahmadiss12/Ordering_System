
namespace OrderingSystem.Domain.Geography;

/// <summary>
/// A platform-level area, not owned by any restaurant — Achrafieh, Hamra, Jounieh. It is the
/// field that actually drives coverage and fees, because Lebanese street addresses are not
/// reliable enough to compute distance from.
/// </summary>
public class DeliveryZone
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public ICollection<RestaurantZone> RestaurantZones { get; } = [];
    public ICollection<Address> Addresses { get; } = [];
}
