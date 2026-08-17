namespace Vastora.Application.Common;

/// <summary>
/// §9.18. Every list endpoint takes one of these. Bounds are enforced here rather than by each
/// validator, so no route can accidentally expose an unbounded read.
/// </summary>
public class PageRequest
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    /// <summary>1-based.</summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    public int Skip => (Page - 1) * PageSize;

    public static PageRequest Of(int page, int pageSize) => new() { Page = page, PageSize = pageSize };

    /// <summary>
    /// For internal callers that genuinely need every row (report aggregation, exports). Explicit
    /// so "I forgot to paginate" and "I meant to read everything" don't look identical in a diff.
    /// </summary>
    public static PageRequest All => new() { Page = 1, PageSize = MaxPageSize };
}

/// <summary>The envelope every paged list endpoint returns instead of a bare array.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;

    public bool HasPreviousPage => Page > 1;

    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector) =>
        new([.. Items.Select(selector)], Page, PageSize, TotalCount);

    public static PagedResult<T> Empty(PageRequest page) => new([], page.Page, page.PageSize, 0);
}

/// <summary>Sort direction for the repository's paged reads.</summary>
public enum SortDirection
{
    Ascending = 1,
    Descending = 2
}
