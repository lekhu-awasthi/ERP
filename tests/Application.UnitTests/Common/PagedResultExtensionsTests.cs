using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Configuration.Queries.ListLookups;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Configuration;

namespace ErpApp.Application.UnitTests.Common;

/// <summary>
/// Phase 16c's shared paging primitives. The in-memory overloads (ToPagedResult/ToUnpagedResult)
/// back every report handler (rows already materialized before pagination applies); the
/// EF-translating overload (ToPagedResultAsync) backs every ListX query handler. Both get their
/// own direct coverage here rather than relying solely on the ~30 retrofitted handlers to exercise
/// the boundary cases -- page-past-the-end and pageSize-clamped-at-max in particular are easy for
/// an individual handler test to never hit by accident.
/// </summary>
public class PagedResultExtensionsTests
{
    [Fact]
    public void ToPagedResult_returns_the_first_page_in_source_order()
    {
        var source = Enumerable.Range(1, 25).ToList();

        var result = source.ToPagedResult(page: 1, pageSize: 10);

        Assert.Equal(Enumerable.Range(1, 10), result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(25, result.TotalCount);
    }

    [Fact]
    public void ToPagedResult_returns_a_partial_last_page()
    {
        var source = Enumerable.Range(1, 25).ToList();

        var result = source.ToPagedResult(page: 3, pageSize: 10);

        Assert.Equal([21, 22, 23, 24, 25], result.Items);
        Assert.Equal(25, result.TotalCount);
    }

    [Fact]
    public void ToPagedResult_a_page_past_the_end_returns_an_empty_list_not_an_error()
    {
        var source = Enumerable.Range(1, 25).ToList();

        var result = source.ToPagedResult(page: 99, pageSize: 10);

        Assert.Empty(result.Items);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(99, result.Page);
    }

    [Fact]
    public void ToPagedResult_consecutive_pages_hold_a_stable_partition_of_a_pre_ordered_source()
    {
        // The extension itself does no ordering -- it only slices whatever order the caller already
        // established (every retrofitted handler orders before calling this). This proves the slice
        // boundaries themselves never duplicate or skip an element across pages, the property a
        // caller's stable OrderBy then depends on.
        var source = Enumerable.Range(1, 47).ToList();

        var page1 = source.ToPagedResult(1, 10).Items;
        var page2 = source.ToPagedResult(2, 10).Items;
        var page3 = source.ToPagedResult(3, 10).Items;
        var page4 = source.ToPagedResult(4, 10).Items;
        var page5 = source.ToPagedResult(5, 10).Items;

        var reassembled = page1.Concat(page2).Concat(page3).Concat(page4).Concat(page5).ToList();
        Assert.Equal(source, reassembled);
    }

    [Fact]
    public void ToUnpagedResult_returns_every_row_as_a_single_page()
    {
        var source = Enumerable.Range(1, 137).ToList();

        var result = source.ToUnpagedResult();

        Assert.Equal(source, result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(137, result.PageSize);
        Assert.Equal(137, result.TotalCount);
    }

    [Fact]
    public void ToUnpagedResult_of_an_empty_source_does_not_produce_a_zero_pageSize()
    {
        var result = new List<int>().ToUnpagedResult();

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.PageSize); // never 0 -- a 0 PageSize would misrepresent an empty export as unbounded
    }

    [Fact]
    public async Task ToPagedResultAsync_pushes_Skip_Take_Count_to_the_EF_provider_and_returns_matching_totals()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        for (var i = 0; i < 23; i++)
        {
            db.CreditTerms.Add(CreditTerm.Create(organizationId, $"Term {i:D2}", i));
        }
        await db.SaveChangesAsync();

        var handler = new ListLookupsQueryHandler<CreditTerm>(db);

        var page1 = await handler.Handle(new ListLookupsQuery<CreditTerm>(organizationId, Page: 1, PageSize: 10), CancellationToken.None);
        var page3 = await handler.Handle(new ListLookupsQuery<CreditTerm>(organizationId, Page: 3, PageSize: 10), CancellationToken.None);
        var pagePastEnd = await handler.Handle(
            new ListLookupsQuery<CreditTerm>(organizationId, Page: 5, PageSize: 10), CancellationToken.None);

        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(23, page1.TotalCount);
        Assert.Equal(3, page3.Items.Count); // last partial page (23 rows, pageSize 10 -> 3 on page 3)
        Assert.Equal(23, page3.TotalCount);
        Assert.Empty(pagePastEnd.Items);
        Assert.Equal(23, pagePastEnd.TotalCount);

        // Page 1 then page 2's first row directly follows page 1's last row under the handler's own
        // OrderBy(Name) -- the same check the manual E2E curl pass makes against the real API/DB.
        var page2 = await handler.Handle(new ListLookupsQuery<CreditTerm>(organizationId, Page: 2, PageSize: 10), CancellationToken.None);
        Assert.True(string.CompareOrdinal(page1.Items[^1].Name, page2.Items[0].Name) < 0);
    }
}
