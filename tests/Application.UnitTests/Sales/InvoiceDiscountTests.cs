using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveCreditNote;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Sales;

/// <summary>Phase 16b: proves a discounted Invoice posts a balanced GL entry matching hand
/// arithmetic (no separate Discount account -- confirmed live against the reference product's GL
/// Transactions panel, Sales Goods credited at the post-discount taxable amount), and that its
/// CreditNote reversal -- built by the same conversion-template/cap flow every other reversal uses
/// -- nets every touched account back to exactly zero across the pair (the docs/phase-6-status.md
/// bug #3 / phase-16a precedent, now re-verified with discount as a variable). Also proves the
/// conversion-cap guards added this phase: a CreditNote can't apply a different transaction- or
/// line-level discount than the source Invoice actually carried.</summary>
public class InvoiceDiscountTests
{
    [Fact]
    public async Task Approve_posts_a_balanced_gl_entry_at_the_post_discount_taxable_amount()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Qty 10 * Rate 1000 = 10,000 gross, 10% line discount -> 9,000 sub total, 5% header
        // discount -> 8,550 taxable, VAT 13% of 8,550 = 1,111.50, Grand Total 9,661.50 -- the same
        // worked example confirmed live against the reference product's Totals panel.
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, new DateOnly(2026, 1, 10), null,
                [new InvoiceLineInput(seed.ProductId, 10m, 1000m, VatRate.ThirteenPercentVat, DiscountPct: 10)],
                DiscountPct: 5),
            CancellationToken.None);

        await ApproveInvoiceAsync(db, seed, created.Id);

        var glEntry = await db.GlJournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.SourceDocumentType == DocumentType.Invoice && x.SourceDocumentId == created.Id);

        Assert.Equal(glEntry.Lines.Sum(l => l.Debit), glEntry.Lines.Sum(l => l.Credit));
        Assert.Contains(glEntry.Lines, l => l.AccountId == seed.AccountsReceivableId && l.Debit == 9661.50m);
        Assert.Contains(glEntry.Lines, l => l.AccountId == seed.SalesAccountId && l.Credit == 8550m);
        Assert.Contains(glEntry.Lines, l => l.AccountId == seed.VatPayableAccountId && l.Credit == 1111.50m);
        Assert.DoesNotContain(glEntry.Lines, l => l.AccountId != seed.AccountsReceivableId
            && l.AccountId != seed.SalesAccountId && l.AccountId != seed.VatPayableAccountId);
    }

    [Fact]
    public async Task CreditNote_reversal_with_the_same_discount_nets_every_account_to_zero()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, new DateOnly(2026, 1, 10), null,
                [new InvoiceLineInput(seed.ProductId, 10m, 1000m, VatRate.ThirteenPercentVat, DiscountPct: 10)],
                DiscountPct: 5),
            CancellationToken.None);
        await ApproveInvoiceAsync(db, seed, created.Id);

        var creditNote = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, new DateOnly(2026, 1, 15), null,
                [new CreditNoteLineInput(seed.ProductId, 10m, 1000m, VatRate.ThirteenPercentVat, DiscountPct: 10)],
                DocumentType.Invoice, created.Id, DiscountPct: 5),
            CancellationToken.None);
        await new ApproveCreditNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, creditNote.Id), CancellationToken.None);

        var invoiceGlLines = await db.GlJournalEntries.Include(x => x.Lines)
            .Where(x => x.SourceDocumentType == DocumentType.Invoice && x.SourceDocumentId == created.Id)
            .SelectMany(x => x.Lines).ToListAsync();
        var creditNoteGlLines = await db.GlJournalEntries.Include(x => x.Lines)
            .Where(x => x.SourceDocumentType == DocumentType.CreditNote && x.SourceDocumentId == creditNote.Id)
            .SelectMany(x => x.Lines).ToListAsync();

        var allLines = invoiceGlLines.Concat(creditNoteGlLines).ToList();
        foreach (var accountId in allLines.Select(l => l.AccountId).Distinct())
        {
            var netDebit = allLines.Where(l => l.AccountId == accountId).Sum(l => l.Debit - l.Credit);
            Assert.Equal(0m, netDebit);
        }
    }

    [Fact]
    public async Task CreditNote_with_a_different_header_discount_than_the_source_invoice_is_rejected()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, new DateOnly(2026, 1, 10), null,
                [new InvoiceLineInput(seed.ProductId, 10m, 1000m, VatRate.ThirteenPercentVat, DiscountPct: 10)],
                DiscountPct: 5),
            CancellationToken.None);
        await ApproveInvoiceAsync(db, seed, created.Id);

        await Assert.ThrowsAsync<ConflictException>(() => new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, new DateOnly(2026, 1, 15), null,
                [new CreditNoteLineInput(seed.ProductId, 10m, 1000m, VatRate.ThirteenPercentVat, DiscountPct: 10)],
                DocumentType.Invoice, created.Id, DiscountPct: 0),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreditNote_with_a_different_line_discount_than_the_source_invoice_is_rejected()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, new DateOnly(2026, 1, 10), null,
                [new InvoiceLineInput(seed.ProductId, 10m, 1000m, VatRate.ThirteenPercentVat, DiscountPct: 10)],
                DiscountPct: 5),
            CancellationToken.None);
        await ApproveInvoiceAsync(db, seed, created.Id);

        await Assert.ThrowsAsync<ConflictException>(() => new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, new DateOnly(2026, 1, 15), null,
                [new CreditNoteLineInput(seed.ProductId, 10m, 1000m, VatRate.ThirteenPercentVat, DiscountPct: 0)],
                DocumentType.Invoice, created.Id, DiscountPct: 5),
            CancellationToken.None));
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid WarehouseId,
        Guid ProductId, Guid SalesAccountId, Guid AccountsReceivableId, Guid VatPayableAccountId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);

        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);

        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "Services", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Unit", "u"), CancellationToken.None);

        var product = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Consulting", category.Id, unit.Id, null, true, 1000m, 800m,
                VatRate.ThirteenPercentVat, 0, false),
            CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var incomeGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);

        var ar = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Receivable", assetGroup.Id), CancellationToken.None);
        var vatPayable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Payable", liabilityGroup.Id), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, null, null, null, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(organizationId, numberGenerator, customer.Id, warehouse.Id, product.Id, sales.Id, ar.Id, vatPayable.Id);
    }

    private static async Task ApproveInvoiceAsync(IAppDbContext db, Seed seed, Guid invoiceId)
    {
        var stockLedgerService = new StockLedgerService(db);
        await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, invoiceId, OverrideWarning: false), CancellationToken.None);
    }
}
