namespace OrderingSystem.Application.Common;

/// <summary>A page of results plus enough to render a pager without a second call.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;
}

/// <summary>
/// Page bounds, applied server-side. An unbounded page size is a denial-of-service invitation:
/// one request asking for every row is cheap to send and expensive to answer.
/// </summary>
public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public static (int Page, int PageSize) Normalise(int? page, int? pageSize) =>
        (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
