using Microsoft.AspNetCore.SignalR;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Api.Realtime;

/// <summary>
/// Pushes an order change to the kitchen watching it and the customer waiting on it.
/// </summary>
public sealed partial class SignalROrderNotifier(
    IHubContext<OrdersHub> hub, ILogger<SignalROrderNotifier> logger) : IOrderNotifier
{
    /// <summary>The client-side handler name. One constant, because a typo here is silence.</summary>
    public const string MethodName = "orderChanged";

    public async Task OrderChangedAsync(
        Guid restaurantId,
        Guid customerId,
        OrderChanged change,
        CancellationToken cancellationToken = default)
    {
        // Both groups in one call, but that is only tidiness: Clients.Groups walks the groups in
        // turn and does not remember connections it has already reached, so it is no protection
        // against a duplicate. What guarantees a screen sees an order once is that the hub puts
        // every connection in exactly one group.
        var groups = new[]
        {
            OrderGroups.ForRestaurant(restaurantId),
            OrderGroups.ForCustomer(customerId),
        };

        try
        {
            await hub.Clients.Groups(groups).SendAsync(MethodName, change, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberately swallowed. The order is already committed, and a broken socket must
            // never turn a placed order into a failed request — the customer would try again and
            // the kitchen would cook it twice. The screen has a poll behind it for exactly this.
            LogPushFailed(logger, ex, change.Status, change.OrderId);
        }
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Error,
        Message = "Could not push {Status} for order {OrderId} to its watchers.")]
    private static partial void LogPushFailed(
        ILogger logger, Exception exception, OrderStatus status, Guid orderId);
}
