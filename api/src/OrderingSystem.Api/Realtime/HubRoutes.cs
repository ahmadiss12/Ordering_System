namespace OrderingSystem.Api.Realtime;

/// <summary>
/// Where the hubs are mapped.
///
/// <para>
/// A constant rather than two literals, because the bearer handler narrows its query-string token
/// rule to this exact path. If the two ever disagreed, the failure is not a broken build — it is
/// either a hub nobody can authenticate against, or bearer tokens accepted from query strings
/// across the whole API.
/// </para>
/// </summary>
internal static class HubRoutes
{
    public const string Orders = "/hubs/orders";
}
