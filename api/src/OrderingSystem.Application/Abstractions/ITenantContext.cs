namespace OrderingSystem.Application.Abstractions;

/// <summary>
/// Who is asking, resolved from the JWT once per request. This is the input to every global query
/// filter, so it is the single most security-sensitive type in the application.
/// </summary>
public interface ITenantContext
{
    /// <summary>Null when nobody is authenticated.</summary>
    Guid? UserId { get; }

    /// <summary>
    /// The restaurant this caller is staff at, from the restaurant_id claim. Null for customers,
    /// for platform admins and for anonymous callers — and a null here means query filters do not
    /// narrow by restaurant, so anything relying on it must say what null implies.
    /// </summary>
    Guid? RestaurantId { get; }

    /// <summary>
    /// Bypasses ownership filters entirely. Set only from the PlatformAdmin role claim, never
    /// from anything a client can influence.
    /// </summary>
    bool IsPlatformAdmin { get; }
}
