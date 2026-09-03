using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Application.Features.Orders;

/// <summary>
/// Moving an order one step along.
/// </summary>
/// <param name="To">
/// Where the order should go, named outright.
/// <para>
/// Deliberately one field rather than four endpoints called accept, reject, advance and cancel.
/// The transition table already decides what may follow what and who may do it; a set of named
/// endpoints would be a second copy of that table, kept in step by hand, and every status added
/// later would need a new one. The detail endpoint hands a screen the moves it can make, and the
/// screen posts one of them straight back.
/// </para>
/// </param>
/// <param name="Reason">
/// Required when a restaurant refuses an order or backs out of one it accepted; refused on any
/// other move, because a value there is what the rejection-rate report counts.
/// <para>
/// A fixed list rather than free text, since a report cannot group by a sentence somebody typed.
/// </para>
/// </param>
/// <param name="Note">
/// Free text alongside the move — "out of buns", "customer rang to change the address". Always
/// optional, and never a substitute for a reason.
/// </param>
public sealed record ChangeOrderStatusRequest(
    OrderStatus To,
    RejectionReason? Reason,
    string? Note);
