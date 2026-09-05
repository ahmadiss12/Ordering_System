namespace OrderingSystem.Application.Abstractions;

/// <summary>
/// The explicit half of ADR-07's isolation. Query filters are the net that catches reads; this is
/// what a write path calls before it changes anything.
/// <para>
/// It exists because a filter is a WHERE clause and an INSERT has no WHERE. Nothing in EF stops
/// code writing a row stamped with another restaurant's id — only a check like this does.
/// </para>
/// </summary>
public interface ITenantGuard
{
    /// <summary>
    /// Throws unless the caller may act for <paramref name="restaurantId"/>. A platform admin may
    /// act for any; a staff member only for their own; nobody else at all.
    /// </summary>
    void EnsureCanActFor(Guid restaurantId);

    /// <summary>The caller's restaurant, or a 403 if they are not staff anywhere.</summary>
    Guid RequireRestaurantId();

    /// <summary>The signed-in user, or a 401 if nobody is.</summary>
    Guid RequireUserId();

    /// <summary>
    /// Throws unless the caller runs the platform rather than a restaurant.
    ///
    /// <para>
    /// Separate from <see cref="EnsureCanActFor"/> because that one admits a restaurant acting on
    /// itself, which is right for a menu and wrong for a commission rate: an owner calling the
    /// platform endpoints with their own restaurant's id would pass it. What a restaurant is
    /// charged, and whether it is listed at all, are the platform's to set and nobody else's.
    /// </para>
    /// </summary>
    void RequirePlatformAdmin();
}
