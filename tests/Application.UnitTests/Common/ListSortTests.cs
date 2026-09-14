using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Sales.Queries.ListInvoices;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Common;

/// <summary>
/// Phase 40 — the chrome's <c>Sort by</c> control, which phase 34b built and shipped with no
/// consumer, and the rule that decides what a screen may offer in it.
///
/// <para>34b's re-entry condition was "the first list whose default ordering someone complains
/// about". Nobody can check that. 34c supplies one that can: an ordering may be offered exactly when
/// an index already leads on <c>(OrganizationId, &lt;that column&gt;)</c>. The invoice list's two
/// options are therefore not a design choice but a reading of <c>TenantIndexConvention</c> —
/// <c>(OrganizationId, CreatedAt DESC)</c> for the list and <c>(OrganizationId, Date)</c> for the
/// date range. That correspondence is <i>not</i> asserted by a test, and the reason is worth stating:
/// this suite references Application only, and the convention lives in Infrastructure. It is recorded
/// where a reader of either side will meet it — in <see cref="ISortableQuery"/> and in the
/// convention's own comment. These tests are the behavioural half: that the orderings differ, that
/// null means the default, and that an unknown value is refused rather than ignored.</para>
/// </summary>
public class ListSortTests
{
    private static readonly DateOnly Earlier = new(2026, 5, 10);
    private static readonly DateOnly Later = new(2026, 5, 20);

    [Fact]
    public async Task The_default_ordering_is_newest_first_and_null_means_the_default()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        await InventoryReportSeed.PurchaseAsync(db, seed, Earlier.AddDays(-5), 100m, 10m);

        // Created in this order, so CreatedAt ascends with creation and descends in the list.
        // `CreatedAt` is stamped `DateTimeOffset.UtcNow` at construction, and two rows built in the
        // same tick would tie — which would make these assertions pass or fail by timer resolution.
        // Separating them through the change tracker is the phase-31 idiom for reaching a state only
        // time produces, rather than weakening anything to get there.
        var first = await InventoryReportSeed.SellAsync(db, seed, Later, 1m, 100m);
        var second = await InventoryReportSeed.SellAsync(db, seed, Earlier, 1m, 100m);
        await SeparateCreatedAtAsync(db, first.Id, second.Id);

        var byDefault = await ListAsync(db, seed.OrganizationId, sort: null);
        var byName = await ListAsync(db, seed.OrganizationId, sort: ListSort.Newest);

        Assert.Equal([second.Id, first.Id], byDefault.Items.Select(x => x.Id));
        Assert.Equal(byDefault.Items.Select(x => x.Id), byName.Items.Select(x => x.Id));
    }

    /// <summary>
    /// The point of offering the second ordering at all: the two disagree, because a document's
    /// business date and the moment it was typed in are different facts. A back-dated invoice
    /// entered today is the newest row and the oldest document.
    /// </summary>
    [Fact]
    public async Task Ordering_by_document_date_is_not_the_same_order_as_newest_first()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        await InventoryReportSeed.PurchaseAsync(db, seed, Earlier.AddDays(-5), 100m, 10m);

        var datedLater = await InventoryReportSeed.SellAsync(db, seed, Later, 1m, 100m);
        var datedEarlier = await InventoryReportSeed.SellAsync(db, seed, Earlier, 1m, 100m);
        await SeparateCreatedAtAsync(db, datedLater.Id, datedEarlier.Id);

        var newest = await ListAsync(db, seed.OrganizationId, ListSort.Newest);
        var byDate = await ListAsync(db, seed.OrganizationId, ListSort.DocumentDate);

        Assert.Equal([datedEarlier.Id, datedLater.Id], newest.Items.Select(x => x.Id));
        Assert.Equal([datedLater.Id, datedEarlier.Id], byDate.Items.Select(x => x.Id));
    }

    /// <summary>
    /// An unrecognised ordering is a 400 naming the field, never a silent fall back to the default.
    /// A list that quietly ignores the ordering it was asked for is the read-side gap phase 35a
    /// spent a section on: the control looks like it works and the rows never change.
    /// </summary>
    [Fact]
    public void An_unknown_ordering_is_rejected_by_the_validator()
    {
        var validator = new ListInvoicesQueryValidator();
        var organizationId = Guid.NewGuid();

        Assert.True(validator.Validate(Query(organizationId, null)).IsValid);
        Assert.True(validator.Validate(Query(organizationId, ListSort.Newest)).IsValid);
        Assert.True(validator.Validate(Query(organizationId, ListSort.DocumentDate)).IsValid);

        var rejected = validator.Validate(Query(organizationId, "customer"));
        Assert.False(rejected.IsValid);
        Assert.Equal(nameof(ListInvoicesQuery.Sort), Assert.Single(rejected.Errors).PropertyName);
    }

    /// <summary>
    /// The wire values are matched by name, and a rename would be a silently broken control on every
    /// screen that sends the old string — the same reason phase 26a bridges enums by name rather
    /// than by ordinal. Pinning the literals is what makes a rename a failing test.
    /// </summary>
    [Fact]
    public void The_wire_values_are_pinned()
    {
        Assert.Equal("newest", ListSort.Newest);
        Assert.Equal("date", ListSort.DocumentDate);
        Assert.Equal([ListSort.DocumentDate, ListSort.Newest], ListSort.DocumentOrderings.Order(StringComparer.Ordinal));
    }

    /// <summary>Stamps two invoices a second apart, oldest first, so "newest" has one answer.</summary>
    private static async Task SeparateCreatedAtAsync(IAppDbContext db, Guid olderId, Guid newerId)
    {
        var baseline = DateTimeOffset.UtcNow.AddMinutes(-5);
        // `IAppDbContext` exposes the sets, not the change tracker -- deliberately, since a handler
        // has no business reaching through it. A test does: this is phase 31's "reach through EF's
        // change tracker rather than weaken a Domain invariant", and the cast is where that is said.
        var context = (TestAppDbContext)db;
        var older = await context.Invoices.FirstAsync(x => x.Id == olderId, CancellationToken.None);
        var newer = await context.Invoices.FirstAsync(x => x.Id == newerId, CancellationToken.None);
        context.Entry(older).Property(nameof(Invoice.CreatedAt)).CurrentValue = baseline;
        context.Entry(newer).Property(nameof(Invoice.CreatedAt)).CurrentValue = baseline.AddSeconds(1);
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static ListInvoicesQuery Query(Guid organizationId, string? sort) =>
        new(organizationId, null, 1, 50, null, null, null, null, sort);

    private static Task<PagedResult<Invoice>> ListAsync(IAppDbContext db, Guid organizationId, string? sort)
    {
        return new ListInvoicesQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            Query(organizationId, sort), CancellationToken.None);
    }
}
