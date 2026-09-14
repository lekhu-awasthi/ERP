using System.Linq.Expressions;
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

    /// <summary>
    /// Phase 42 -- the same page, taken without fetching the rows it skips.
    ///
    /// <para><b>What the measurement found.</b> On the 50,000-invoice dataset the last page of a
    /// list cost 153,705 logical reads and 513 ms of SQL against the first page's 166 reads and
    /// 6 ms, and a search term matching nothing cost 946 ms. Neither figure is the offset and
    /// neither is the <c>LIKE</c>: both are the <i>row fetch</i>. <c>ToPagedResultAsync</c> asks
    /// SQL Server for whole entities in the same statement that orders and offsets them, so the
    /// engine takes the ordering index and does a key lookup into the clustered index for every
    /// row it is about to throw away -- 50,000 of them at the tail, and every row in the table for
    /// a term that matches none.</para>
    ///
    /// <para><b>The shape.</b> Count, then take the page's <i>keys</i> from an index that already
    /// carries them, then fetch exactly those rows. Ids only means the first two statements are
    /// index-only: the same tail is 571 reads instead of 153,705. It is
    /// <c>JournalReportQueryHandler</c>'s shape -- page the keys, fetch detail for the page --
    /// applied to a list instead of a report, and it fixes phase-34c's carried items #5 (the
    /// offset tail) and #6 (search on a term matching nothing) with one mechanism, inside the
    /// existing <see cref="PagedResult{T}"/> envelope. Keyset pagination would have changed that
    /// envelope, every list screen and both sweep guards; it is not needed.</para>
    ///
    /// <para><b>The empty short-circuit is half the win.</b> A count of zero is a complete answer:
    /// there is no page to ask for. That one branch is what takes a no-match search from 946 ms to
    /// the cost of the count alone, and it is why nothing here needs to know whether a search term
    /// was applied -- a filter that matches nothing behaves the same way whatever it filtered on
    /// (<c>status=Draft</c> on this dataset matches nothing either, and cost 386 ms).</para>
    ///
    /// <para><b>Every condition travels with <paramref name="filtered"/>.</b> This helper composes
    /// onto the caller's query and never rebuilds one, so it cannot drop the caller's
    /// <c>OrganizationId</c> the way a helper that took a <c>DbSet</c> and re-derived the filters
    /// could (phase-35b's rule). The final statement re-applies both the filter and the ordering
    /// alongside the key restriction, so the page's rows arrive in the page's order.</para>
    /// </summary>
    /// <param name="filtered">The query with every condition applied and no ordering.</param>
    /// <param name="key">The entity's primary key -- an <see cref="Expression"/>, never a captured
    /// <c>Func</c>, so EF translates it rather than refusing the <c>Where</c> (phase-9 bug #1).</param>
    /// <param name="order">The ordering, applied to the key query and again to the row query.</param>
    public static async Task<PagedResult<T>> ToKeyPagedResultAsync<T>(
        this IQueryable<T> filtered,
        Expression<Func<T, Guid>> key,
        Func<IQueryable<T>, IOrderedQueryable<T>> order,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var totalCount = await filtered.CountAsync(cancellationToken);
        var skip = (page - 1) * pageSize;

        // Nothing matched, or the caller asked for a page past the end. Either way the page is
        // empty and asking SQL Server for it would be asking a question already answered.
        if (totalCount == 0 || skip >= totalCount)
        {
            return new PagedResult<T>([], page, pageSize, totalCount);
        }

        var pageKeys = await order(filtered)
            .Select(key)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        if (pageKeys.Count == 0)
        {
            return new PagedResult<T>([], page, pageSize, totalCount);
        }

        var items = await order(filtered.Where(KeyIn(key, pageKeys))).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, page, pageSize, totalCount);
    }

    /// <summary>
    /// <c>x =&gt; keys.Contains(key(x))</c>, built from the caller's key expression rather than
    /// from a captured <c>Func</c>. The list is at most one page long, so this is never the
    /// 50,000-element <c>OPENJSON</c> parameter phase 34c warned about -- that gotcha is about
    /// handing a whole period's ids back to SQL, which is the thing this helper exists to stop.
    /// </summary>
    private static Expression<Func<T, bool>> KeyIn<T>(Expression<Func<T, Guid>> key, List<Guid> keys)
    {
        var contains = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(Guid)],
            Expression.Constant(keys),
            key.Body);

        return Expression.Lambda<Func<T, bool>>(contains, key.Parameters);
    }

    public static PagedResult<T> ToUnpagedResult<T>(this IReadOnlyList<T> source) =>
        new(source, 1, Math.Max(source.Count, 1), source.Count);
}
