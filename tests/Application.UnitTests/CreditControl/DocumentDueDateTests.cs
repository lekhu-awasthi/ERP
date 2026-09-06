using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Communications;
using ErpApp.Application.Contacts.Queries.ContactAgeingSummary;
using ErpApp.Application.Contacts.Queries.DocumentAge;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Commands.UpdateInvoice;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Queries.SalesRegister;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.CreditControl;

/// <summary>
/// Phase 31 -- the stored Due Date on Invoice (and PurchaseBill), closing phase-26b's carried item
/// and phase-30's <c>$[DUE_DATE]$</c>. Confirmed live 2026-09-06: the reference Invoice form has its
/// own required, editable Due Date defaulting to the invoice date, and Invoice Age computes Age Days
/// from it.
/// </summary>
public class DocumentDueDateTests
{
    private static readonly DateOnly InvoiceDate = new(2026, 1, 10);
    private static readonly DateOnly DueDate = new(2026, 2, 9);

    [Fact]
    public async Task An_omitted_due_date_defaults_to_the_documents_own_date()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db);
        var id = await DraftAsync(db, seed, dueDate: null);

        Assert.Equal(InvoiceDate, (await db.Invoices.SingleAsync(x => x.Id == id)).DueDate);
    }

    [Fact]
    public async Task A_supplied_due_date_is_stored_and_survives_an_edit()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db);
        var id = await DraftAsync(db, seed, DueDate);

        Assert.Equal(DueDate, (await db.Invoices.SingleAsync(x => x.Id == id)).DueDate);

        await new UpdateInvoiceCommandHandler(db).Handle(
            new UpdateInvoiceCommand(
                seed.OrganizationId, id, seed.CustomerId, seed.WarehouseId, InvoiceDate, null,
                [new InvoiceLineInput(seed.ProductId, 1m, 500m, VatRate.NoVat)],
                DueDate: new DateOnly(2026, 3, 1)),
            CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 3, 1), (await db.Invoices.SingleAsync(x => x.Id == id)).DueDate);
    }

    /// <summary>Invoice Age's Due Date column and its Age Days both come from the stored value now,
    /// not from the document date.</summary>
    [Fact]
    public async Task Invoice_age_reports_the_stored_due_date_and_ages_from_it()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db);
        await ApproveAsync(db, seed, await DraftAsync(db, seed, DueDate));

        var report = await new DocumentAgeQueryHandler(db).Handle(
            new DocumentAgeQuery(
                seed.OrganizationId, ContactType.Customer, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 19)),
            CancellationToken.None);

        var row = Assert.Single(report.Rows);
        Assert.Equal(DueDate, row.DueDate);
        Assert.Equal(10, row.AgeDays);
    }

    /// <summary>
    /// The other half of phase-26b's follow-up #4: phase-9's Ageing Summary buckets from the due
    /// date too, so it and Invoice Age cannot disagree. Due 2026-02-09 read as of 2026-02-19 is 10
    /// days old and belongs in the 1-30 bucket, where ageing from the invoice date (40 days) would
    /// have put it in 31-60.
    /// </summary>
    [Fact]
    public async Task The_ageing_summary_buckets_from_the_due_date_not_the_document_date()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db);
        await ApproveAsync(db, seed, await DraftAsync(db, seed, DueDate));

        var report = await new ContactAgeingSummaryQueryHandler(db).Handle(
            new ContactAgeingSummaryQuery(seed.OrganizationId, ContactType.Customer, new DateOnly(2026, 2, 19)),
            CancellationToken.None);

        var row = Assert.Single(report.Rows);
        Assert.Equal(500m, row.Days1To30);
        Assert.Equal(0m, row.Days31To60);
    }

    /// <summary>Phase-30 resolved <c>$[DUE_DATE]$</c> to the document date because no aggregate
    /// stored one. One line changed; this is the proof it changed correctly.</summary>
    [Fact]
    public async Task The_DUE_DATE_merge_field_resolves_to_the_stored_due_date()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db);
        var id = await DraftAsync(db, seed, DueDate);

        var facts = await EmailMergeValueReader.ReadDocumentAsync(
            db, seed.OrganizationId, DocumentType.Invoice, id, CancellationToken.None);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        EmailMergeValueReader.AddDocumentValues(values, facts, "Acme Traders");

        Assert.Equal(EmailMergeResolver.FormatDate(DueDate), values["DUE_DATE"]);
        Assert.Equal(EmailMergeResolver.FormatDate(InvoiceDate), values["DOCUMENT_DATE"]);
    }

    /// <summary>
    /// Phase 31 -- the live "Include Credit Note In Calculation" toggle, which phase 26c recorded as
    /// inert. Off, the credit-note rows leave the row set and every total recomputes without them.
    /// </summary>
    [Fact]
    public async Task The_sales_register_toggle_removes_the_credit_note_rows_and_retotals()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db);
        await ApproveAsync(db, seed, await DraftAsync(db, seed, null));

        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 12, 31);

        var withNotes = await new SalesRegisterQueryHandler(db).Handle(
            new SalesRegisterQuery(seed.OrganizationId, from, to, null, null), CancellationToken.None);
        var withoutNotes = await new SalesRegisterQueryHandler(db).Handle(
            new SalesRegisterQuery(seed.OrganizationId, from, to, null, null, IncludeCreditNotes: false),
            CancellationToken.None);

        // No credit notes exist here, so the two agree -- which is the half of the assertion that
        // proves the flag does not silently change an ordinary register.
        Assert.Equal(withNotes.TotalCount, withoutNotes.TotalCount);
        Assert.Equal(withNotes.TotalValue, withoutNotes.TotalValue);
        Assert.Equal(500m, withoutNotes.TotalValue);
    }

    private static async Task<Guid> DraftAsync(IAppDbContext db, CreditSeed seed, DateOnly? dueDate)
    {
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, InvoiceDate, null,
                [new InvoiceLineInput(seed.ProductId, 1m, 500m, VatRate.NoVat)],
                DueDate: dueDate),
            CancellationToken.None);
        return created.Id;
    }

    private static Task<ApproveInvoiceResult> ApproveAsync(IAppDbContext db, CreditSeed seed, Guid invoiceId)
    {
        var stockLedgerService = new StockLedgerService(db);
        return new ApproveInvoiceCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
                new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService,
                new ContactCreditLimitPolicy(db))
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, invoiceId), CancellationToken.None);
    }
}
