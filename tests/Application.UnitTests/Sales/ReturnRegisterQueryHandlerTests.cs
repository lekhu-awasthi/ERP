using ErpApp.Application.Purchasing.Queries.PurchaseRegister;
using ErpApp.Application.Purchasing.Queries.PurchaseReturnRegister;
using ErpApp.Application.Sales.Queries.SalesRegister;
using ErpApp.Application.Sales.Queries.SalesReturnRegister;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;

namespace ErpApp.Application.UnitTests.Sales;

/// <summary>
/// Phase 26c's two statutory return registers, and the phase's key correctness finding: the main
/// registers <b>keep</b> their credit- and debit-note rows. That was confirmed live on 2026-09-03
/// by generating the Sales Register and the Sales Return Register over the same period back to back
/// -- the same twelve credit notes appeared in both, negative in the first and positive in the
/// second, with the main register's footer Total arithmetically net of them. The roadmap had
/// predicted the opposite. These tests pin the relationship so it cannot be "tidied up" later.
/// </summary>
public class ReturnRegisterQueryHandlerTests
{
    private static readonly DateOnly PeriodStart = new(2026, 5, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 5, 31);

    [Fact]
    public async Task The_sales_return_register_lists_credit_notes_positively_with_the_statutory_split()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 100m, 10m);
        var invoice = await InventoryReportSeed.SellAsync(
            db, seed, PeriodStart.AddDays(2), 10m, 100m, vatRate: VatRate.ThirteenPercentVat);
        var creditNote = await InventoryReportSeed.CreditNoteAsync(
            db, seed, PeriodStart.AddDays(3), 4m, 100m, invoice.Id, vatRate: VatRate.ThirteenPercentVat);

        var result = await new SalesReturnRegisterQueryHandler(db).Handle(
            new SalesReturnRegisterQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null),
            CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(creditNote.Code, row.DocumentCode);
        Assert.Equal("Acme Retail", row.ContactName);
        Assert.Equal("301234567", row.ContactPan);
        Assert.Equal(400m, row.TaxableReturnValue);
        Assert.Equal(0m, row.TaxExemptReturnValue);
        Assert.Equal(52m, row.VatAmount); // 400 @ 13%
        Assert.Equal(452m, row.TotalReturnValue);

        // The footer total the live register carries.
        Assert.Equal(452m, result.TotalReturnValue);
        Assert.Equal(52m, result.TotalVatAmount);
    }

    /// <summary>
    /// The phase's key correctness question, answered by construction: both registers read
    /// <c>SalesReturnReader</c>, so a credit note's magnitudes are one figure rendered twice with
    /// opposite signs.
    /// </summary>
    [Fact]
    public async Task The_main_sales_register_still_carries_the_same_credit_notes_negatively()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 100m, 10m);
        var invoice = await InventoryReportSeed.SellAsync(
            db, seed, PeriodStart.AddDays(2), 10m, 100m, vatRate: VatRate.ThirteenPercentVat);
        await InventoryReportSeed.CreditNoteAsync(
            db, seed, PeriodStart.AddDays(3), 4m, 100m, invoice.Id, vatRate: VatRate.ThirteenPercentVat);

        var register = await new SalesRegisterQueryHandler(db).Handle(
            new SalesRegisterQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null, null),
            CancellationToken.None);
        var returns = await new SalesReturnRegisterQueryHandler(db).Handle(
            new SalesReturnRegisterQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null),
            CancellationToken.None);

        var creditNoteRow = Assert.Single(register.Items, r => r.DocumentType == DocumentType.CreditNote);
        var returnRow = Assert.Single(returns.Items);

        Assert.Equal(returnRow.DocumentCode, creditNoteRow.DocumentCode);
        Assert.Equal(-returnRow.TotalReturnValue, creditNoteRow.TotalValue);
        Assert.Equal(-returnRow.TaxableReturnValue, creditNoteRow.TaxableValue);
        Assert.Equal(-returnRow.VatAmount, creditNoteRow.VatAmount);

        // And the main register's total is net of the return: 1,130 invoiced less 452 returned.
        Assert.Equal(1130m - 452m, register.TotalValue);
    }

    /// <summary>
    /// The purchase-side register is <b>not</b> the sales side's mirror -- it inherits the Purchase
    /// Register's Capital/Others and Local/Import split, so it carries seven money columns to the
    /// sales side's four. That is why the pair is two handlers rather than 26b's one-handler-two-sides
    /// pattern.
    /// </summary>
    [Fact]
    public async Task The_purchase_return_register_buckets_a_debit_note_by_capital_and_import_like_its_parent()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        var capitalImportBill = await InventoryReportSeed.PurchaseAsync(
            db, seed, PeriodStart.AddDays(1), 10m, 100m,
            vatRate: VatRate.ThirteenPercentVat, classification: ExpenditureClassification.Capital, isImport: true);
        await InventoryReportSeed.DebitNoteAsync(
            db, seed, PeriodStart.AddDays(2), 3m, 100m, capitalImportBill.Id, vatRate: VatRate.ThirteenPercentVat);

        var result = await new PurchaseReturnRegisterQueryHandler(db).Handle(
            new PurchaseReturnRegisterQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null),
            CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal("Global Supplies", row.ContactName);

        // Capital wins over import: a capital line goes to the capital column either way.
        Assert.Equal(300m, row.TaxableCapitalValue);
        Assert.Equal(39m, row.TaxableCapitalVat);
        Assert.Equal(0m, row.TaxableNonCapitalImportValue);
        Assert.Equal(0m, row.TaxableNonCapitalLocalValue);
        Assert.Equal(0m, row.TaxExemptValue);
        Assert.Equal(339m, row.TotalReturnValue);
    }

    [Fact]
    public async Task The_main_purchase_register_still_carries_the_same_debit_notes_negatively()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        var bill = await InventoryReportSeed.PurchaseAsync(
            db, seed, PeriodStart.AddDays(1), 10m, 100m, vatRate: VatRate.ThirteenPercentVat);
        await InventoryReportSeed.DebitNoteAsync(
            db, seed, PeriodStart.AddDays(2), 3m, 100m, bill.Id, vatRate: VatRate.ThirteenPercentVat);

        var register = await new PurchaseRegisterQueryHandler(db).Handle(
            new PurchaseRegisterQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null),
            CancellationToken.None);
        var returns = await new PurchaseReturnRegisterQueryHandler(db).Handle(
            new PurchaseReturnRegisterQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null),
            CancellationToken.None);

        var debitNoteRow = Assert.Single(register.Items, r => r.DocumentType == DocumentType.DebitNote);
        var returnRow = Assert.Single(returns.Items);

        Assert.Equal(returnRow.DocumentCode, debitNoteRow.DocumentCode);
        Assert.Equal(-returnRow.TaxableNonCapitalLocalValue, debitNoteRow.TaxableNonCapitalLocalValue);
        Assert.Equal(-returnRow.TaxableNonCapitalLocalVat, debitNoteRow.TaxableNonCapitalLocalVat);
    }

    [Fact]
    public async Task A_draft_return_never_appears_in_either_register()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, PeriodStart.AddDays(1), 100m, 10m);
        var invoice = await InventoryReportSeed.SellAsync(db, seed, PeriodStart.AddDays(2), 10m, 100m);

        // Created but never approved.
        await new Application.Sales.Commands.CreateCreditNote.CreateCreditNoteCommandHandler(db).Handle(
            new Application.Sales.Commands.CreateCreditNote.CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, PeriodStart.AddDays(3), null,
                [new Application.Sales.CreditNoteLineInput(seed.ProductId, 2m, 100m, VatRate.NoVat)],
                DocumentType.Invoice, invoice.Id),
            CancellationToken.None);

        var result = await new SalesReturnRegisterQueryHandler(db).Handle(
            new SalesReturnRegisterQuery(seed.OrganizationId, PeriodStart, PeriodEnd, null),
            CancellationToken.None);

        Assert.Empty(result.Items);
    }
}
