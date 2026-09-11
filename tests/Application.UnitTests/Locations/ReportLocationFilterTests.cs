using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Cash;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Commands.VoidJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Accounting.Queries.TrialBalance;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Application.Workflow.Queries.TransactionList;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Locations;

/// <summary>
/// Phase 35b -- that the Billing Location filter <b>bites</b>, which
/// <c>ReportLocationSweepGuardTests</c> cannot see.
///
/// <para>That guard proves every report query declares the filter and every handler can read the
/// permission scope. Neither is the same as the filter changing which rows come back, and the
/// difference is exactly phase 35a's lesson repeated: a declaration is not a behaviour. These tests
/// take one report per mechanism -- the GL side, the document side, and the permission scope -- and
/// assert the rows actually move.</para>
///
/// <para><b>Why the GL case is the important one.</b> It is the only family whose filter depends on
/// a column this phase invented. If <c>ApproveJournalVoucherCommandHandler</c> ever stops copying
/// the voucher's location onto its <c>GlJournalEntry</c>, every one of the nine GL reports silently
/// returns nothing for every branch -- and nothing else in the suite would notice.</para>
/// </summary>
public class ReportLocationFilterTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task A_gl_report_filtered_to_one_location_shows_only_that_locations_postings()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var (headOffice, branch) = await SeedTwoLocationsAsync(db, organizationId);

        await ApproveVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 1000m, headOffice);
        await ApproveVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 250m, branch);

        Assert.Equal(1250m, await TrialBalanceDebitAsync(db, organizationId, cashAccountId, locationId: null));
        Assert.Equal(1000m, await TrialBalanceDebitAsync(db, organizationId, cashAccountId, headOffice));
        Assert.Equal(250m, await TrialBalanceDebitAsync(db, organizationId, cashAccountId, branch));
    }

    /// <summary>
    /// The stamp is what makes the filter possible, so it is asserted on the row rather than only
    /// through the report: a report that returned the right numbers because *both* entries were
    /// unfiltered would pass the test above for one wrong reason.
    /// </summary>
    [Fact]
    public async Task Approving_a_document_stamps_its_location_onto_the_posted_gl_entry()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var (_, branch) = await SeedTwoLocationsAsync(db, organizationId);

        await ApproveVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 400m, branch);

        var entry = await db.GlJournalEntries.SingleAsync(CancellationToken.None);
        Assert.Equal(branch, entry.LocationId);
    }

    /// <summary>
    /// A void posts a mirror entry rather than mutating the original (phase 16a). It has to land at
    /// the same location, or the branch's Trial Balance stays permanently off by the document's
    /// value while the organization-wide total still balances -- phase-6 bug #3 with the location as
    /// the axis instead of an account.
    /// </summary>
    [Fact]
    public async Task A_void_reverses_at_the_same_location_so_the_branch_nets_to_zero()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var (_, branch) = await SeedTwoLocationsAsync(db, organizationId);

        var voucherId = await ApproveVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 700m, branch);
        Assert.Equal(700m, await TrialBalanceDebitAsync(db, organizationId, cashAccountId, branch));

        await new VoidJournalVoucherCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new VoidJournalVoucherCommand(organizationId, voucherId), CancellationToken.None);

        Assert.Equal(2, await db.GlJournalEntries.CountAsync(CancellationToken.None));
        Assert.All(
            await db.GlJournalEntries.ToListAsync(CancellationToken.None),
            entry => Assert.Equal(branch, entry.LocationId));
        Assert.Equal(0m, await TrialBalanceDebitAsync(db, organizationId, cashAccountId, branch));
    }

    /// <summary>
    /// A document-sourced report goes through <c>ReportLocationFilter.AtLocations</c> rather than the
    /// GL column, so it is worth one assertion of its own -- the two mechanisms share nothing but the
    /// argument list.
    /// </summary>
    [Fact]
    public async Task A_document_sourced_report_filters_on_the_documents_own_location()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var (headOffice, branch) = await SeedTwoLocationsAsync(db, organizationId);

        await ApproveVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 1000m, headOffice);
        await ApproveVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 250m, branch);

        Assert.Equal(2, (await TransactionListAsync(db, organizationId, locationId: null)).Items.Count);

        var branchOnly = await TransactionListAsync(db, organizationId, branch);
        Assert.Equal(250m, Assert.Single(branchOnly.Items).Amount);
    }

    /// <summary>
    /// <c>TenantSettings.LocationWiseReportPermission</c> narrows rows without the caller asking, and
    /// is a different mechanism from the filter: it reads the caller's <i>grants</i>. Phase 32b built
    /// it and left it with one consumer; this is the assertion that it now governs the reports.
    /// </summary>
    [Fact]
    public async Task The_report_permission_scope_narrows_rows_with_no_filter_requested()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var (headOffice, branch) = await SeedTwoLocationsAsync(db, organizationId);

        await ApproveVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 1000m, headOffice);
        await ApproveVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 250m, branch);

        var clerk = Guid.NewGuid();
        await PermissionGrantSeed.GrantAsync(db, organizationId, clerk);
        db.RolePermissions.Add(RolePermission.Create(
            Guid.NewGuid(), Role.AdminId, PermissionKeys.TrialBalanceView, true, branch));
        await db.SaveChangesAsync(CancellationToken.None);

        // Off (the default): the clerk's grant is irrelevant and they see the whole organization.
        Assert.Equal(1250m, await TrialBalanceDebitAsync(db, organizationId, cashAccountId, null, clerk));

        var settings = await db.TenantSettings.SingleAsync(x => x.OrganizationId == organizationId, CancellationToken.None);
        settings.SetLocationSettings(settings.LocationScopeMode, locationWiseReportPermission: true);
        await db.SaveChangesAsync(CancellationToken.None);

        // On: the same request, the same data, and only the branch they hold a grant at.
        Assert.Equal(250m, await TrialBalanceDebitAsync(db, organizationId, cashAccountId, null, clerk));

        // And asking for a location outside that scope is empty rather than an error -- the two
        // Where clauses simply both apply.
        Assert.Equal(0m, await TrialBalanceDebitAsync(db, organizationId, cashAccountId, headOffice, clerk));
    }

    private static async Task<(Guid HeadOffice, Guid Branch)> SeedTwoLocationsAsync(IAppDbContext db, Guid organizationId)
    {
        var headOffice = BillingLocation.CreateHeadOffice(organizationId);
        var branch = BillingLocation.Create(organizationId, "BR1", "Branch One", null, null);
        db.BillingLocations.Add(headOffice);
        db.BillingLocations.Add(branch);

        db.TenantSettings.Add(TenantSettings.CreateDefault(organizationId));
        await db.SaveChangesAsync(CancellationToken.None);

        var settings = await db.TenantSettings.SingleAsync(x => x.OrganizationId == organizationId, CancellationToken.None);
        settings.SetLocationSettings(LocationScopeMode.AllTransactions, locationWiseReportPermission: false);
        await db.SaveChangesAsync(CancellationToken.None);

        return (headOffice.Id, branch.Id);
    }

    private static async Task<Guid> ApproveVoucherAsync(
        IAppDbContext db, Guid organizationId, Guid debitAccountId, Guid creditAccountId, decimal amount, Guid locationId)
    {
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, Today, null,
                [new JournalVoucherLineInput(debitAccountId, amount, 0m), new JournalVoucherLineInput(creditAccountId, 0m, amount)])
            {
                LocationId = locationId,
            },
            CancellationToken.None);

        await new ApproveJournalVoucherCommandHandler(
                db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()),
                new JournalVoucherPostingRule(), new GlCashBalancePolicy(db))
            .Handle(new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);

        return created.Id;
    }

    private static async Task<decimal> TrialBalanceDebitAsync(
        IAppDbContext db, Guid organizationId, Guid accountId, Guid? locationId, Guid? userId = null)
    {
        var result = await new TrialBalanceQueryHandler(db, new FakeCurrentUserService(userId ?? Guid.NewGuid()))
            .Handle(new TrialBalanceQuery(organizationId, Today, LocationId: locationId), CancellationToken.None);

        return Assert.Single(result.Rows, r => r.AccountId == accountId).Debit;
    }

    private static Task<Application.Common.Pagination.PagedResult<TransactionListRowDto>> TransactionListAsync(
        IAppDbContext db, Guid organizationId, Guid? locationId) =>
        new TransactionListQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new TransactionListQuery(
                organizationId,
                [DocumentType.JournalVoucher],
                null,
                null,
                null,
                LocationId: locationId),
            CancellationToken.None);
}
