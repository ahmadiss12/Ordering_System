using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Enums;

namespace OrderingSystem.Application.Features.Reports;

/// <summary>
/// What the rejection reasons were collected for.
///
/// <para>
/// Everything here is grouped by the order's own business date rather than by <c>PlacedAt</c>,
/// which is UTC. Beirut runs two or three hours ahead depending on the season, so a UTC grouping
/// would file part of every evening under the previous day — and differently in winter and
/// summer, which is the kind of wrongness nobody notices and nobody can explain afterwards.
/// </para>
/// <para>
/// Money comes from each order's own snapshot, never from the restaurant's current settings. A
/// report of last month has to say what was actually charged, whatever the commission rate has
/// done since.
/// </para>
/// </summary>
public sealed class RestaurantReportService(
    IAppDbContext db, ITenantGuard guard, IValidationService validation, IClock clock)
{
    public async Task<RestaurantReportResponse> SummaryAsync(
        ReportRangeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        var restaurantId = guard.RequireRestaurantId();
        var (from, to) = Resolve(request);

        // Aggregated in the database rather than by pulling the orders back. A year of a busy
        // restaurant is tens of thousands of rows, and none of them is wanted here - only five
        // numbers per day.
        var rows = await db.Orders.AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId
                && o.BusinessDate >= from
                && o.BusinessDate <= to)
            .GroupBy(o => o.BusinessDate)
            .Select(day => new
            {
                Date = day.Key,
                Orders = day.Count(),
                Rejected = day.Count(o => o.Status == OrderStatus.Rejected),
                Cancelled = day.Count(o => o.Status == OrderStatus.Cancelled),
                // Spelled out rather than calling a helper: this becomes SQL, and EF cannot
                // translate a method call. Written as one it would throw at query time — after
                // compiling perfectly.
                RevenueUsd = day.Sum(o =>
                    o.Status != OrderStatus.Rejected && o.Status != OrderStatus.Cancelled
                        ? o.TotalUsd
                        : 0m),
                CommissionUsd = day.Sum(o =>
                    o.Status != OrderStatus.Rejected && o.Status != OrderStatus.Cancelled
                        ? o.CommissionUsd
                        : 0m),
            })
            .ToListAsync(ct);

        var byDate = rows.ToDictionary(r => r.Date);

        // Every day in the range, present or not. A chart that skipped the empty ones would put
        // Monday next to Thursday and look like an unremarkable week.
        var days = new List<ReportDay>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            days.Add(byDate.TryGetValue(date, out var row)
                ? new ReportDay(date, row.Orders, row.Rejected, row.RevenueUsd, row.CommissionUsd)
                : new ReportDay(date, 0, 0, 0m, 0m));
        }

        var orders = rows.Sum(r => r.Orders);
        var rejected = rows.Sum(r => r.Rejected);
        var cancelled = rows.Sum(r => r.Cancelled);
        var revenue = rows.Sum(r => r.RevenueUsd);
        // Neither refused nor withdrawn. Still cooking counts: it is going to be paid for, and a
        // report that waited for delivery would read as empty right through service.
        var kept = orders - rejected - cancelled;

        var totals = new ReportTotals(
            orders,
            kept,
            rejected,
            cancelled,
            revenue,
            rows.Sum(r => r.CommissionUsd),
            // Averaged over the orders that produced the revenue, not over every order placed.
            // Dividing by a count that includes refusals would report an average nobody was
            // ever charged, and would fall every time the kitchen had a bad week.
            kept == 0 ? 0m : decimal.Round(revenue / kept, 2, MidpointRounding.AwayFromZero),
            orders == 0 ? 0m : decimal.Round((decimal)rejected / orders, 4, MidpointRounding.AwayFromZero));

        return new RestaurantReportResponse(from, to, totals, days, await RejectionsAsync(restaurantId, from, to, ct));
    }

    /// <summary>
    /// Why orders were refused, commonest first.
    ///
    /// <para>
    /// A separate query rather than a projection of the one above, because it groups by a
    /// different thing entirely. Reasons do not belong to a day.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<RejectionBreakdown>> RejectionsAsync(
        Guid restaurantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.Orders.AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId
                && o.BusinessDate >= from
                && o.BusinessDate <= to
                && o.Status == OrderStatus.Rejected
                && o.RejectionReason != null)
            .GroupBy(o => o.RejectionReason!.Value)
            .Select(reason => new { Reason = reason.Key, Count = reason.Count() })
            .ToListAsync(ct);

        var total = rows.Sum(r => r.Count);

        return
        [
            .. rows
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.Reason)
                .Select(r => new RejectionBreakdown(
                    r.Reason,
                    r.Count,
                    // Of the refusals, not of all orders. "Half of what you turn away is because
                    // you have run out" is the sentence a restaurant can act on.
                    total == 0 ? 0m : decimal.Round((decimal)r.Count / total, 4, MidpointRounding.AwayFromZero))),
        ];
    }

    /// <summary>
    /// Fills in whichever end the caller left out. Defaulting to the last 30 days including today
    /// means the screen has something to show before anybody touches a date field.
    /// </summary>
    private (DateOnly From, DateOnly To) Resolve(ReportRangeRequest request)
    {
        var to = request.To ?? clock.LocalToday;
        var from = request.From ?? to.AddDays(-(ReportRangeRequestValidator.DefaultDays - 1));

        return (from, to);
    }
}
