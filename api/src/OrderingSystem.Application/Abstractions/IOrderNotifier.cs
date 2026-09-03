using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Application.Abstractions;

/// <summary>
/// What a live screen is told when an order changes. Deliberately small.
///
/// <para>
/// It carries an identity and a status, not the order. Two reasons, and both matter more than the
/// extra round trip it costs: a push that carried names, prices and addresses would be a second
/// copy of the order contract, kept in step by hand and never passed through the query filters
/// that decide who may see what — so a mistake in a group name would leak a customer's address
/// rather than an id they already have. And a payload goes stale the moment it is sent, while an
/// id stays true; a screen that refetches shows what is in the database, not what was in the
/// database when the message left.
/// </para>
/// </summary>
/// <param name="PreviousStatus">Null when the order has just been placed and came from nowhere.</param>
public sealed record OrderChanged(
    Guid OrderId,
    string OrderNumber,
    OrderStatus Status,
    OrderStatus? PreviousStatus,
    DateTimeOffset At);

/// <summary>
/// Tells whoever is watching that an order moved.
///
/// <para>
/// The two ids are routing, not payload: they decide which groups hear about this, and the client
/// is never sent them. Keeping them out of <see cref="OrderChanged"/> is what makes it impossible
/// to widen the message by accident — there is nothing in it to widen.
/// </para>
/// <para>
/// Implementations must not throw. A socket that has gone away cannot un-place an order that is
/// already committed, and turning that into a failed request would be the worst of both.
/// </para>
/// </summary>
public interface IOrderNotifier
{
    Task OrderChangedAsync(
        Guid restaurantId,
        Guid customerId,
        OrderChanged change,
        CancellationToken cancellationToken = default);
}
