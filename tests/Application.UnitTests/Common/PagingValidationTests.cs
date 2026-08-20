using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Sales.Queries.ListInvoices;

namespace ErpApp.Application.UnitTests.Common;

/// <summary>
/// Phase 16c's shared paging validation rule, exercised through one representative retrofitted
/// query's own validator (ListInvoicesQueryValidator) -- every other retrofitted query's validator
/// is a one-line call to the same `this.ValidatePaging(...)` extension, so this is the rule itself,
/// not a coincidence of ListInvoices specifically. Reject, never clamp, per
/// phase-16c-status.md's boundary-correctness decision.
/// </summary>
public class PagingValidationTests
{
    private readonly ListInvoicesQueryValidator validator = new();

    [Fact]
    public void Page_zero_is_rejected()
    {
        var result = validator.Validate(new ListInvoicesQuery(Guid.NewGuid(), null, Page: 0, PageSize: 50));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListInvoicesQuery.Page));
    }

    [Fact]
    public void Negative_page_is_rejected()
    {
        var result = validator.Validate(new ListInvoicesQuery(Guid.NewGuid(), null, Page: -1, PageSize: 50));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListInvoicesQuery.Page));
    }

    [Fact]
    public void PageSize_above_the_max_is_rejected_not_silently_clamped()
    {
        var result = validator.Validate(
            new ListInvoicesQuery(Guid.NewGuid(), null, Page: 1, PageSize: PagingDefaults.MaxPageSize + 1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListInvoicesQuery.PageSize));
    }

    [Fact]
    public void PageSize_zero_is_rejected()
    {
        var result = validator.Validate(new ListInvoicesQuery(Guid.NewGuid(), null, Page: 1, PageSize: 0));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListInvoicesQuery.PageSize));
    }

    [Fact]
    public void PageSize_exactly_at_the_max_is_accepted()
    {
        var result = validator.Validate(
            new ListInvoicesQuery(Guid.NewGuid(), null, Page: 1, PageSize: PagingDefaults.MaxPageSize));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Default_page_and_pageSize_are_valid()
    {
        var result = validator.Validate(new ListInvoicesQuery(Guid.NewGuid(), null));

        Assert.True(result.IsValid);
    }
}
