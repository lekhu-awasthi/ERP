using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Pagination;

/// <summary>
/// Two paging strategies, matching the two shapes of query this phase retrofits (see
/// phase-16c-status.md): most of the 22 ListX queries filter a single EF Core IQueryable, so
/// Skip/Take/CountAsync push to SQL (ToPagedResultAsync). The 8 report queries build their final
/// row set as an in-memory List after several joined round-trips -- paginating there means
/// slicing that materialized list, not composing more LINQ (ToPagedResult). ToUnpagedResult backs
/// a report's ExportAll=true path: same filters, same permission gate, paging ignored.
/// </summary>
public static class PagedResultExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> orderedQuery, int page, int pageSize, CancellationToken cancellationToken)
    {
        var totalCount = await orderedQuery.CountAsync(cancellationToken);
        var items = await orderedQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, page, pageSize, totalCount);
    }

    public static PagedResult<T> ToPagedResult<T>(this IReadOnlyList<T> orderedSource, int page, int pageSize)
    {
        var items = orderedSource.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<T>(items, page, pageSize, orderedSource.Count);
    }

    public static PagedResult<T> ToUnpagedResult<T>(this IReadOnlyList<T> source) =>
        new(source, 1, Math.Max(source.Count, 1), source.Count);
}
