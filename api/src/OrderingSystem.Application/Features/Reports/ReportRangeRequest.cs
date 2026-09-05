namespace OrderingSystem.Application.Features.Reports;

/// <summary>
/// A range of the restaurant's own calendar days, both ends included.
/// </summary>
/// <param name="From">Omit for a range ending at <paramref name="To"/> and covering the last month.</param>
/// <param name="To">Omit for today, in the restaurant's timezone rather than the caller's.</param>
public sealed record ReportRangeRequest(DateOnly? From, DateOnly? To);
