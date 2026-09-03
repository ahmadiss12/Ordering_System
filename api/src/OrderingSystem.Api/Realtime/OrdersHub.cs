using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OrderingSystem.Api.Auth;

namespace OrderingSystem.Api.Realtime;

/// <summary>
/// The live channel a kitchen screen and a customer's order tracker listen on.
///
/// <para>
/// It has no methods a client can call, and that is the design rather than an omission. A hub
/// method taking a group name would let any connection ask to join <c>restaurant:{someone else}</c>
/// — the whole isolation model undone by one string parameter. Membership is decided here, from
/// claims on a token this server signed, and a client's only power is to connect or not.
/// </para>
/// <para>
/// The groups mirror the query filters on <c>Order</c>: staff hear their restaurant's orders, a
/// customer hears their own. Nobody is subscribed to everything, including a platform admin — a
/// firehose is not a feature anybody asked for, and it would be the one connection worth stealing.
/// </para>
/// </summary>
[Authorize]
public sealed partial class OrdersHub(ILogger<OrdersHub> logger) : Hub
{
    // Claims come from Context.User rather than from ITenantContext. That reads the ambient
    // HttpContext, which a controller always has and a hub callback does not dependably — and a
    // tenant context that quietly resolved to null here would put the connection in no group at
    // all, which looks exactly like a screen that simply never updates.
    public override async Task OnConnectedAsync()
    {
        var userId = TenantClaims.UserIdOf(Context.User);
        var restaurantId = TenantClaims.RestaurantIdOf(Context.User);

        // One group, never two, and the either/or is the same one the query filter on Order
        // makes: a caller with a restaurant claim sees that restaurant's orders *instead of*
        // their own, so the restaurant group is already everything they are entitled to hear.
        //
        // It also has to be one group rather than two. SignalR's Clients.Groups sends to each
        // group in turn without tracking connections it has already reached, so a cook ordering
        // their own lunch would have had the same order pushed to their screen twice.
        if (restaurantId is { } restaurant)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, OrderGroups.ForRestaurant(restaurant));
        }
        else if (userId is { } user)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, OrderGroups.ForCustomer(user));
        }

        if (userId is null)
        {
            // [Authorize] should have stopped this, so reaching it means the token carried no
            // subject — a connection that will silently hear nothing. Worth a line in the log
            // rather than a mystery in a kitchen.
            LogNoSubject(logger, Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    // No OnDisconnectedAsync. SignalR removes a connection from its groups when it goes away, and
    // an override that repeated that would only be a place for it to be done wrong.

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Warning,
        Message = "Connection {ConnectionId} reached the orders hub with no user id and joined no group.")]
    private static partial void LogNoSubject(ILogger logger, string connectionId);
}
