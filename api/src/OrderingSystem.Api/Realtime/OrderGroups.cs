namespace OrderingSystem.Api.Realtime;

/// <summary>
/// The names of the SignalR groups orders are pushed to, in one place.
///
/// <para>
/// One place on purpose. The hub decides which groups a connection joins and the notifier decides
/// which groups a message goes to, and those two answers have to be the same string. Two copies
/// that drift apart do not fail loudly — one side simply stops hearing, or worse, starts hearing
/// somebody else's orders. Neither shows up as an error anywhere.
/// </para>
/// <para>
/// The prefixes keep the two kinds of id apart. Without them a restaurant whose id happened to
/// equal a user's id would share a group, which is a coincidence that should not be possible to
/// rely on not happening.
/// </para>
/// </summary>
internal static class OrderGroups
{
    /// <summary>Every staff member signed in at this restaurant.</summary>
    public static string ForRestaurant(Guid restaurantId) =>
        $"restaurant:{restaurantId}";

    /// <summary>Every device this customer is signed in on.</summary>
    public static string ForCustomer(Guid userId) =>
        $"customer:{userId}";
}
