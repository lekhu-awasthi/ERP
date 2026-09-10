using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Contacts.Queries.ListContacts;
using ErpApp.Application.Sales.Queries.ListInvoices;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;

namespace ErpApp.Application.UnitTests.Common;

/// <summary>
/// Phase 34b — what the search term and the global date range actually <i>do</i>.
///
/// <para><c>SearchSweepGuardTests</c> proves every paginated list declares a term and bounds it;
/// that is a shape check and would pass just as happily against a handler that ignored the term. So
/// these are the behavioural half, one per shape: master data matched on name and code, and a
/// document matched on number and reference and bracketed by its own business date.</para>
///
/// <para><b>A note on casing that matters for every test written after this one.</b> The predicate
/// these handlers use is the single-argument <c>string.Contains</c>, which SQL Server renders as
/// <c>LIKE '%term%'</c> and therefore matches case-<i>insensitively</i> under the default collation.
/// The InMemory provider these tests run on evaluates the same expression in C#, where it is
/// case-<b>sensitive</b>. So a test here must search with the stored casing: asserting that "acme"
/// finds "Acme" would fail here while working in production, and asserting that it does <i>not</i>
/// find it would pin a behaviour the real database does not have.</para>
/// </summary>
public class ListSearchAndDateRangeTests
{
    private static readonly DateOnly Day1 = new(2026, 5, 10);
    private static readonly DateOnly Day2 = new(2026, 5, 20);

    [Fact]
    public async Task A_master_data_search_matches_the_name_and_the_code()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var numbers = new FakeDocumentNumberGenerator();

        var acme = await CreateContactAsync(db, numbers, organizationId, "Acme Retail");
        await CreateContactAsync(db, numbers, organizationId, "Borealis Trading");

        var byName = await ListContactsAsync(db, organizationId, "Acme");
        Assert.Equal(acme.Id, Assert.Single(byName.Items).Id);

        // The generated code is the other half of what a user types into a list search box.
        var byCode = await ListContactsAsync(db, organizationId, acme.Code);
        Assert.Equal(acme.Id, Assert.Single(byCode.Items).Id);

        var noMatch = await ListContactsAsync(db, organizationId, "Nothing here");
        Assert.Empty(noMatch.Items);
    }

    /// <summary>
    /// The rule <see cref="SearchTerm.Normalize"/> exists for: a box someone tabbed through is not a
    /// search. Without it every list in the app would return nothing the moment a space was typed.
    /// </summary>
    [Fact]
    public async Task A_blank_or_whitespace_term_is_not_a_filter()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var numbers = new FakeDocumentNumberGenerator();

        await CreateContactAsync(db, numbers, organizationId, "Acme Retail");
        await CreateContactAsync(db, numbers, organizationId, "Borealis Trading");

        Assert.Equal(2, (await ListContactsAsync(db, organizationId, null)).TotalCount);
        Assert.Equal(2, (await ListContactsAsync(db, organizationId, "")).TotalCount);
        Assert.Equal(2, (await ListContactsAsync(db, organizationId, "   ")).TotalCount);

        // And a term with incidental whitespace is still that term.
        Assert.Equal(1, (await ListContactsAsync(db, organizationId, "  Acme  ")).TotalCount);
    }

    [Fact]
    public async Task The_search_narrows_the_total_count_not_just_the_page()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var numbers = new FakeDocumentNumberGenerator();

        for (var i = 0; i < 5; i++)
        {
            await CreateContactAsync(db, numbers, organizationId, $"Acme Depot {i}");
        }

        await CreateContactAsync(db, numbers, organizationId, "Borealis Trading");

        var result = await ListContactsAsync(db, organizationId, "Acme", pageSize: 2);

        // Phase-16c's rule restated for search: a footer figure must describe the whole filtered
        // set, never the page. If TotalCount were 6 here the pager would offer pages of results the
        // filter has already excluded.
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task A_document_search_matches_the_number_and_the_reference()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, Day1.AddDays(-5), 100m, 10m);
        var first = await InventoryReportSeed.SellAsync(db, seed, Day1, 1m, 100m);
        var second = await InventoryReportSeed.SellAsync(db, seed, Day2, 1m, 100m);

        var byNumber = await ListInvoicesAsync(db, seed.OrganizationId, search: first.Code);
        Assert.Equal(first.Id, Assert.Single(byNumber.Items).Id);

        // A reference is nullable, which is why the predicate guards it -- a null Reference on the
        // *other* row must not make the whole comparison throw or drop the matching row.
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task The_date_range_brackets_on_the_documents_own_business_date()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, Day1.AddDays(-5), 100m, 10m);
        var early = await InventoryReportSeed.SellAsync(db, seed, Day1, 1m, 100m);
        var late = await InventoryReportSeed.SellAsync(db, seed, Day2, 1m, 100m);

        // Both bounds are inclusive, which is what "This Fiscal Year to Date" means when today is
        // the ToDate: a document dated today has to be in it.
        var justEarly = await ListInvoicesAsync(db, seed.OrganizationId, from: Day1, to: Day1);
        Assert.Equal(early.Id, Assert.Single(justEarly.Items).Id);

        var justLate = await ListInvoicesAsync(db, seed.OrganizationId, from: Day2, to: Day2);
        Assert.Equal(late.Id, Assert.Single(justLate.Items).Id);

        var both = await ListInvoicesAsync(db, seed.OrganizationId, from: Day1, to: Day2);
        Assert.Equal(2, both.TotalCount);

        // An open-ended range is a real case: the shell's presets always set both, but a caller
        // may send one.
        Assert.Equal(2, (await ListInvoicesAsync(db, seed.OrganizationId, from: Day1)).TotalCount);
        Assert.Equal(1, (await ListInvoicesAsync(db, seed.OrganizationId, to: Day1)).TotalCount);

        // And the range filters on Date, not on CreatedAt -- these rows were all created just now,
        // so a handler filtering the wrong column would return everything for every window.
        Assert.Empty((await ListInvoicesAsync(db, seed.OrganizationId, from: Day2.AddDays(1))).Items);
    }

    [Fact]
    public async Task The_search_and_the_range_compose_rather_than_replacing_each_other()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, Day1.AddDays(-5), 100m, 10m);
        var early = await InventoryReportSeed.SellAsync(db, seed, Day1, 1m, 100m);
        await InventoryReportSeed.SellAsync(db, seed, Day2, 1m, 100m);

        // The early invoice's own number, but a window that excludes it: the two filters are
        // separate composed `.Where()` calls, so this must be empty rather than "found by number".
        var contradictory = await ListInvoicesAsync(
            db, seed.OrganizationId, search: early.Code, from: Day2, to: Day2);

        Assert.Empty(contradictory.Items);

        var agreeing = await ListInvoicesAsync(
            db, seed.OrganizationId, search: early.Code, from: Day1, to: Day2);

        Assert.Equal(early.Id, Assert.Single(agreeing.Items).Id);
    }

    private static async Task<CreateContactResult> CreateContactAsync(
        IAppDbContext db, FakeDocumentNumberGenerator numbers, Guid organizationId, string name)
    {
        return await new CreateContactCommandHandler(db, numbers)
            .Handle(
                new CreateContactCommand(
                    organizationId, ContactType.Customer, name, null, null, null, null, null, 0m),
                CancellationToken.None);
    }

    private static Task<PagedResult<Contact>> ListContactsAsync(
        IAppDbContext db, Guid organizationId, string? search, int pageSize = 50)
    {
        return new ListContactsQueryHandler(db).Handle(
            new ListContactsQuery(organizationId, null, 1, pageSize, search),
            CancellationToken.None);
    }

    private static Task<PagedResult<Invoice>> ListInvoicesAsync(
        IAppDbContext db,
        Guid organizationId,
        string? search = null,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        return new ListInvoicesQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new ListInvoicesQuery(organizationId, null, 1, 50, null, search, from, to),
            CancellationToken.None);
    }
}
