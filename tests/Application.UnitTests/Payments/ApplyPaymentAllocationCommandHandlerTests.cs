using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Payments;
using ErpApp.Application.Payments.Commands.ApplyPaymentAllocation;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Posting;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Payments;

public class ApplyPaymentAllocationCommandHandlerTests
{
    [Fact]
    public async Task Handle_applies_more_of_an_under_allocated_approved_payment()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoice = await CreateAndApproveInvoiceAsync(db, seed, 1000m);

        // Quick Receipt: 1000 received, zero allocation at Approve time (Phase 17 decision #1).
        var created = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                seed.OrganizationId, seed.CustomerId, PaymentDirection.Received, new DateOnly(2026, 1, 1), null,
                seed.CashAccountId, 1000m, null, []),
            CancellationToken.None);
        await new ApprovePaymentCommandHandler(db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PaymentPostingRule())
            .Handle(new ApprovePaymentCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        var handler = new ApplyPaymentAllocationCommandHandler(db);
        var result = await handler.Handle(
            new ApplyPaymentAllocationCommand(
                seed.OrganizationId, DocumentType.Payment, created.Id, null, DocumentType.Invoice, invoice.Id, 400m),
            CancellationToken.None);

        Assert.Equal(400m, result.Allocated);
        Assert.Equal(600m, result.Balance);
    }

    [Fact]
    public async Task Handle_throws_when_allocation_would_exceed_the_payments_amount()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoice = await CreateAndApproveInvoiceAsync(db, seed, 1000m);

        var created = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                seed.OrganizationId, seed.CustomerId, PaymentDirection.Received, new DateOnly(2026, 1, 1), null,
                seed.CashAccountId, 300m, null, []),
            CancellationToken.None);
        await new ApprovePaymentCommandHandler(db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PaymentPostingRule())
            .Handle(new ApprovePaymentCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        var handler = new ApplyPaymentAllocationCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new ApplyPaymentAllocationCommand(
                seed.OrganizationId, DocumentType.Payment, created.Id, null, DocumentType.Invoice, invoice.Id, 400m),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_when_payment_is_still_draft()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoice = await CreateAndApproveInvoiceAsync(db, seed, 1000m);

        var created = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                seed.OrganizationId, seed.CustomerId, PaymentDirection.Received, new DateOnly(2026, 1, 1), null,
                seed.CashAccountId, 300m, null, []),
            CancellationToken.None);

        var handler = new ApplyPaymentAllocationCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new ApplyPaymentAllocationCommand(
                seed.OrganizationId, DocumentType.Payment, created.Id, null, DocumentType.Invoice, invoice.Id, 100m),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_applies_a_journal_voucher_lines_credit_side_for_a_customer()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoice = await CreateAndApproveInvoiceAsync(db, seed, 1000m);

        // A JV crediting the customer's AR account 300 (debiting Cash) -- decision #2's "Credit
        // side is the available amount for a Customer-tagged line" convention.
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(seed.OrganizationId, new DateOnly(2026, 1, 1), null,
                [
                    new JournalVoucherLineInput(seed.CashAccountId, 300m, 0m),
                    new JournalVoucherLineInput(seed.ArAccountId, 0m, 300m, seed.CustomerId),
                ]),
            CancellationToken.None);
        await new ApproveJournalVoucherCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        var line = await db.JournalVoucherLines.SingleAsync(x => x.JournalVoucherId == created.Id && x.ContactId != null);

        var handler = new ApplyPaymentAllocationCommandHandler(db);
        var result = await handler.Handle(
            new ApplyPaymentAllocationCommand(
                seed.OrganizationId, DocumentType.JournalVoucher, line.Id, created.Id, DocumentType.Invoice, invoice.Id, 200m),
            CancellationToken.None);

        Assert.Equal(300m, result.Amount);
        Assert.Equal(200m, result.Allocated);
        Assert.Equal(100m, result.Balance);
    }

    [Fact]
    public async Task Handle_throws_when_journal_voucher_line_allocation_would_exceed_its_own_amount()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoice = await CreateAndApproveInvoiceAsync(db, seed, 1000m);

        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(seed.OrganizationId, new DateOnly(2026, 1, 1), null,
                [
                    new JournalVoucherLineInput(seed.CashAccountId, 300m, 0m),
                    new JournalVoucherLineInput(seed.ArAccountId, 0m, 300m, seed.CustomerId),
                ]),
            CancellationToken.None);
        await new ApproveJournalVoucherCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        var line = await db.JournalVoucherLines.SingleAsync(x => x.JournalVoucherId == created.Id && x.ContactId != null);

        var handler = new ApplyPaymentAllocationCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new ApplyPaymentAllocationCommand(
                seed.OrganizationId, DocumentType.JournalVoucher, line.Id, created.Id, DocumentType.Invoice, invoice.Id, 400m),
            CancellationToken.None));
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid WarehouseId,
        Guid ProductId, Guid CashAccountId, Guid ArAccountId);

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
            new CreateProductCategoryCommand(organizationId, "General", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Piece", "pc"), CancellationToken.None);
        var product = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Consulting", category.Id, unit.Id, null, true, 150m, 100m,
                VatRate.NoVat, 0, false),
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
        var cash = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", assetGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, null, null, null, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(organizationId, numberGenerator, customer.Id, warehouse.Id, product.Id, cash.Id, ar.Id);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveInvoiceAsync(IAppDbContext db, Seed seed, decimal rate)
    {
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, new DateOnly(2026, 1, 1), null,
                [new InvoiceLineInput(seed.ProductId, 1m, rate, VatRate.NoVat)]),
            CancellationToken.None);

        var stockLedgerService = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
