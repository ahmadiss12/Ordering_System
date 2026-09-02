namespace OrderingSystem.Application.Abstractions;

/// <summary>
/// Hands out the next order number for one restaurant on one business day.
///
/// <para>
/// Its own abstraction because it cannot be done with change tracking. Two checkouts a
/// millisecond apart must not read the same counter, and EF's read-modify-write does exactly
/// that — the guarantee needs a single statement holding a lock, which is a database concern and
/// lives in Infrastructure.
/// </para>
/// <para>
/// Gaps are expected and harmless. A checkout that fails after allocating consumes a number no
/// order ever uses, which would matter for invoice numbering that some jurisdictions require to
/// be gapless, and does not matter here.
/// </para>
/// </summary>
public interface IOrderNumberAllocator
{
    /// <summary>The next value for this pair, starting at 1 on a day's first order.</summary>
    Task<int> NextAsync(Guid restaurantId, DateOnly businessDate, CancellationToken ct = default);
}
