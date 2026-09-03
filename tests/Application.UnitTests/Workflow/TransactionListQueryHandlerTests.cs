using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Application.Workflow.Queries.TransactionList;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Workflow;

namespace ErpApp.Application.UnitTests.Workflow;

/// <summary>
/// Phase 26a -- the Transaction list report, whose columns and filters were read live on
/// 2026-09-02 (see docs/phase-26a-status.md).
/// </summary>
public class TransactionListQueryHandlerTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Handle_lists_a_draft_alongside_an_approved_document()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await ApproveJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);
        await CreateDraftJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 250m);

        var result = await new TransactionListQueryHandler(db).Handle(
            new TransactionListQuery(organizationId), CancellationToken.None);

        // Unlike every register report, this one shows Drafts -- the live report's Status filter
        // offers Draft and Approved, which only makes sense if unfiltered means both.
        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, r => r.Status == TransactionListStatus.Draft);
        Assert.Contains(result.Items, r => r.Status == TransactionListStatus.Approved);
    }

    [Fact]
    public async Task Handle_filters_by_status()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await ApproveJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);
        await CreateDraftJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 250m);

        var result = await new TransactionListQueryHandler(db).Handle(
            new TransactionListQuery(organizationId, Statuses: [TransactionListStatus.Draft]), CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(TransactionListStatus.Draft, row.Status);
        Assert.Equal(250m, row.Amount);
    }

    [Fact]
    public async Task Handle_filters_by_document_type()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await ApproveJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var matching = await new TransactionListQueryHandler(db).Handle(
            new TransactionListQuery(organizationId, DocumentTypes: [DocumentType.JournalVoucher]), CancellationToken.None);
        var other = await new TransactionListQueryHandler(db).Handle(
            new TransactionListQuery(organizationId, DocumentTypes: [DocumentType.Invoice]), CancellationToken.None);

        Assert.Single(matching.Items);
        Assert.Empty(other.Items);
    }

    [Fact]
    public async Task Handle_reports_a_journal_vouchers_debit_side_as_its_amount()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await ApproveJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var result = await new TransactionListQueryHandler(db).Handle(
            new TransactionListQuery(organizationId), CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(1000m, row.Amount);
        Assert.NotNull(row.ApprovedByUserId);
        Assert.NotNull(row.ApprovedAt);
    }

    [Fact]
    public async Task Handle_derives_created_by_from_the_audit_trail_and_reports_null_when_there_is_none()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var documentId = await CreateDraftJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 250m);

        var withoutAudit = await new TransactionListQueryHandler(db).Handle(
            new TransactionListQuery(organizationId), CancellationToken.None);

        // No transactional aggregate stores a creator, so with no audit row there is nothing honest
        // to report -- and inventing one from ApprovedByUserId would be worse than a blank.
        Assert.Null(Assert.Single(withoutAudit.Items).CreatedByUserId);

        var user = User.Register("Ramesh Adhikari", "ramesh@example.com", "9800000000", "hash");
        db.Users.Add(user);
        db.Audits.Add(Audit.Create(organizationId, user.Id, "Create", DocumentType.JournalVoucher, documentId));
        await db.SaveChangesAsync(CancellationToken.None);

        var withAudit = await new TransactionListQueryHandler(db).Handle(
            new TransactionListQuery(organizationId), CancellationToken.None);

        var row = Assert.Single(withAudit.Items);
        Assert.Equal(user.Id, row.CreatedByUserId);
        Assert.Equal("Ramesh Adhikari", row.CreatedByName);
    }

    [Fact]
    public async Task Handle_filters_by_date_range()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await ApproveJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var inRange = await new TransactionListQueryHandler(db).Handle(
            new TransactionListQuery(organizationId, FromDate: Today.AddDays(-1), ToDate: Today.AddDays(1)),
            CancellationToken.None);
        var outOfRange = await new TransactionListQueryHandler(db).Handle(
            new TransactionListQuery(organizationId, FromDate: Today.AddDays(-10), ToDate: Today.AddDays(-5)),
            CancellationToken.None);

        Assert.Single(inRange.Items);
        Assert.Empty(outOfRange.Items);
    }

    /// <summary>
    /// The handler maps each document type's own status onto the shared report status <b>by name</b>
    /// -- never by ordinal -- and its doc comment claims every one of the thirteen enums is a
    /// by-name subset of TransactionListStatus. This is that claim, asserted: a future enum member
    /// added to one document type without adding it here fails a test rather than silently throwing
    /// at runtime or, worse, reporting the wrong state.
    /// </summary>
    [Fact]
    public void Every_document_types_status_enum_is_a_by_name_subset_of_the_shared_report_status()
    {
        Type[] statusEnums =
        [
            typeof(QuotationStatus), typeof(SalesOrderStatus), typeof(InvoiceStatus), typeof(CreditNoteStatus),
            typeof(PurchaseOrderStatus), typeof(PurchaseBillStatus), typeof(ExpenseStatus), typeof(DebitNoteStatus),
            typeof(JournalVoucherStatus), typeof(CashTransferStatus), typeof(WarehouseTransferStatus),
            typeof(InventoryAdjustmentStatus), typeof(PaymentStatus),
        ];

        var shared = Enum.GetNames<TransactionListStatus>().ToHashSet();

        foreach (var statusEnum in statusEnums)
        {
            foreach (var name in Enum.GetNames(statusEnum))
            {
                Assert.True(
                    shared.Contains(name),
                    $"{statusEnum.Name}.{name} has no counterpart in TransactionListStatus -- add it there.");
            }
        }
    }

    private static async Task<Guid> CreateDraftJournalVoucherAsync(
        IAppDbContext db, Guid organizationId, Guid debitAccountId, Guid creditAccountId, decimal amount)
    {
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, DateOnly.FromDateTime(DateTime.UtcNow), null,
                [new JournalVoucherLineInput(debitAccountId, amount, 0m), new JournalVoucherLineInput(creditAccountId, 0m, amount)]),
            CancellationToken.None);
        return created.Id;
    }

    private static async Task ApproveJournalVoucherAsync(
        IAppDbContext db, Guid organizationId, Guid debitAccountId, Guid creditAccountId, decimal amount)
    {
        var id = await CreateDraftJournalVoucherAsync(db, organizationId, debitAccountId, creditAccountId, amount);

        await new ApproveJournalVoucherCommandHandler(
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(organizationId, id), CancellationToken.None);
    }
}
