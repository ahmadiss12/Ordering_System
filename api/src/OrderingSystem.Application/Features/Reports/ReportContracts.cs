using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Application.Features.Reports;

/// <summary>
/// What a restaurant did over a range of its own calendar days.
/// </summary>
/// <param name="Days">
/// Every day in the range, including the ones with nothing on them. A day left out would draw as
/// a gap, and a gap reads as missing data rather than as a quiet Tuesday.
/// </param>
public sealed record RestaurantReportResponse(
    DateOnly From,
    DateOnly To,
    ReportTotals Totals,
    IReadOnlyList<ReportDay> Days,
    IReadOnlyList<RejectionBreakdown> Rejections);

/// <param name="Orders">Every order placed in the range, however it ended.</param>
/// <param name="Kept">
/// Orders that were neither refused nor withdrawn — the ones that were, or still are going to be,
/// cooked and paid for.
/// </param>
/// <param name="RevenueUsd">
/// The total charged across <paramref name="Kept"/> orders, delivery fee included.
///
/// <para>
/// Not restricted to delivered ones. A report that only counted finished orders would read as
/// almost nothing all through service and only become true after closing, which is the opposite
/// of when somebody looks at it. Refused and cancelled orders are excluded because they are not
/// sales, and they are counted separately so the numbers still reconcile.
/// </para>
/// </param>
/// <param name="CommissionUsd">
/// What the platform charged on those orders, from each order's own snapshot rather than from the
/// restaurant's current rate — so a report of last month does not move when the rate changes.
/// </param>
/// <param name="RejectionRate">
/// Refused orders over all orders placed, 0 to 1. Cancellations are not in the numerator: a
/// customer changing their mind is not the restaurant refusing them.
/// </param>
public sealed record ReportTotals(
    int Orders,
    int Kept,
    int Rejected,
    int Cancelled,
    decimal RevenueUsd,
    decimal CommissionUsd,
    decimal AverageOrderUsd,
    decimal RejectionRate);

public sealed record ReportDay(
    DateOnly Date,
    int Orders,
    int Rejected,
    decimal RevenueUsd,
    decimal CommissionUsd);

/// <param name="Share">
/// This reason's share of the refusals, 0 to 1 — not of all orders. "Half of what you turn away
/// is because you have run out" is the sentence a restaurant can act on.
/// </param>
public sealed record RejectionBreakdown(RejectionReason Reason, int Count, decimal Share);
