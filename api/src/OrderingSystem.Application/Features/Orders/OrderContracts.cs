using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Application.Features.Orders;

/// <summary>
/// A request to move one order to the next status. <see cref="RejectionReason"/> is required when
/// <see cref="To"/> is Rejected and refused otherwise, so the pair cannot disagree.
/// </summary>
public sealed record AdvanceOrderStatusRequest(
    OrderStatus To,
    RejectionReason? RejectionReason = null,
    string? Note = null);

/// <summary>
/// The order's new state, plus what it may become next. Returning the legal successors saves the
/// dashboard from re-implementing the transition table in TypeScript and drifting from it.
/// </summary>
public sealed record OrderStatusResponse(
    Guid OrderId,
    string OrderNumber,
    OrderStatus Status,
    IReadOnlyList<OrderStatus> AllowedNext,
    DateTimeOffset ChangedAt);
