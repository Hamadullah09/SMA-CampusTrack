using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Application.Common.Models;

/// <summary>One page of results plus the counts a client needs to render a pager.</summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public int TotalCount { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Empty(int page = 1, int pageSize = 25) =>
        new() { Items = [], Page = page, PageSize = pageSize, TotalCount = 0 };
}

/// <summary>Paging, sorting and search parameters shared by every list endpoint.</summary>
public class PagedQuery
{
    private const int MaxPageSize = 200;
    private int _pageSize = 25;
    private int _page = 1;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>Capped server-side so a client cannot ask for the entire table in one call.</summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch { < 1 => 25, > MaxPageSize => MaxPageSize, _ => value };
    }

    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }
}

public static class QueryableExtensions
{
    /// <summary>
    /// Materialises one page. Runs the count and the page as two queries deliberately:
    /// window-function counting is slower on MySQL for the wide joins used here.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;

        var total = await query.CountAsync(ct);
        var items = total == 0
            ? []
            : await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<T> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public static IQueryable<T> ApplyPaging<T>(this IQueryable<T> query, PagedQuery q) =>
        query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize);
}
