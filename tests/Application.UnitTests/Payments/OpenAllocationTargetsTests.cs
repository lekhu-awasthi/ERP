using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Cash;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Payments;
using ErpApp.Application.Payments.Commands.ApplyPaymentAllocation;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Posting;
using ErpApp.Application.Payments.Queries.GetDefaultPaymentAllocations;
using ErpApp.Application.Payments.Queries.ListOpenAllocationTargets;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Payments;

/// <summary>
/// Phase 88 -- the allocation picker lists a contact's documents with something left to settle, in the payment's
/// currency, and the FIFO Suggest fills exactly those. One customer is given every way a document stops being open: a
/// receipt settling it in full, a part payment, a journal voucher's allocation (which Suggest used to ignore), a
/// draft receipt (which settles nothing yet), plus another customer's invoice and a dollar invoice.
/// </summary>
public sealed class OpenAllocationTargetsTests
{
    private static readonly DateOnly Day = new(2026, 10, 12);

    private sealed record World(
        InventoryReportSeed.Seed Seed, Guid Paid, Guid PartPaid, Guid SettledByVoucher, Guid OtherCustomers, Guid Dollar);

    [Fact]
    public async Task Only_the_contacts_open_documents_in_the_payments_currency_are_listed_with_what_is_left()
    {
        var db = TestAppDbContext.Create();
        var world = await BuildAsync(db);

        var npr = await ListAsync(db, world.Seed, CurrencyCatalog.BaseCode);
        var row = Assert.Single(npr);
        Assert.Equal(world.PartPaid, row.Id);
        Assert.Equal(600m, row.Amount);
        Assert.Equal(500m, row.Balance); // 100 received; the draft's 50 settles nothing yet
        Assert.Equal(DocumentType.Invoice, row.TargetDocumentType);

        var usd = await ListAsync(db, world.Seed, "USD");
        Assert.Equal(world.Dollar, Assert.Single(usd).Id);
        Assert.Equal(100m, usd[0].Balance); // in the invoice's own currency, not folded to NPR
    }

    [Fact]
    public async Task Suggest_fills_only_what_the_picker_lists_and_skips_an_invoice_a_journal_voucher_settled()
    {
        var db = TestAppDbContext.Create();
        var world = await BuildAsync(db);

        var suggested = await new GetDefaultPaymentAllocationsQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(
                new GetDefaultPaymentAllocationsQuery(
                    world.Seed.OrganizationId, world.Seed.CustomerId, 1000m, PaymentDirection.Received, CurrencyCatalog.BaseCode),
                CancellationToken.None);

        var only = Assert.Single(suggested);
        Assert.Equal(world.PartPaid, only.TargetDocumentId);
        Assert.Equal(500m, only.Amount);
    }

    [Fact]
    public async Task A_contact_with_nothing_open_lists_nothing()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);

        Assert.Empty(await ListAsync(db, seed, CurrencyCatalog.BaseCode));
    }

    [Fact]
    public void The_validators_name_a_missing_currency()
    {
        var list = new ListOpenAllocationTargetsQueryValidator().Validate(
            new ListOpenAllocationTargetsQuery(Guid.NewGuid(), Guid.NewGuid(), PaymentDirection.Received, string.Empty));
        Assert.Contains(list.Errors, e => e.PropertyName == "CurrencyCode");

        var suggest = new GetDefaultPaymentAllocationsQueryValidator().Validate(
            new GetDefaultPaymentAllocationsQuery(Guid.NewGuid(), Guid.NewGuid(), 10m, PaymentDirection.Received, string.Empty));
        Assert.Contains(suggest.Errors, e => e.PropertyName == "CurrencyCode");
    }

    private static Task<IReadOnlyList<Application.Payments.Allocation.OpenAllocationTargetDto>> ListAsync(
        IAppDbContext db, InventoryReportSeed.Seed seed, string currency) =>
        new ListOpenAllocationTargetsQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new ListOpenAllocationTargetsQuery(seed.OrganizationId, seed.CustomerId, PaymentDirection.Received, currency),
            CancellationToken.None);

    private static async Task<World> BuildAsync(IAppDbContext db)
    {
        var seed = await InventoryReportSeed.CreateAsync(db);
        var cash = Account.Create(seed.OrganizationId, "1010", "Cash In Hand", AccountRootType.Asset, Guid.NewGuid(), AccountKind.Cash);
        db.Accounts.Add(cash);
        await db.SaveChangesAsync();
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 20m, 100m);

        var paid = await InventoryReportSeed.SellAsync(db, seed, Day.AddDays(-3), 4m, 150m);
        var partPaid = await InventoryReportSeed.SellAsync(db, seed, Day.AddDays(-2), 4m, 150m);
        var settledByVoucher = await InventoryReportSeed.SellAsync(db, seed, Day.AddDays(-4), 4m, 150m);

        await PayAsync(db, seed, cash, 600m, approve: true, new PaymentAllocationInput(DocumentType.Invoice, paid.Id, 600m));
        await PayAsync(db, seed, cash, 100m, approve: true, new PaymentAllocationInput(DocumentType.Invoice, partPaid.Id, 100m));
        await PayAsync(db, seed, cash, 50m, approve: false, new PaymentAllocationInput(DocumentType.Invoice, partPaid.Id, 50m));

        // A journal voucher crediting the customer 600, allocated in full to the third invoice.
        var receivable = (await db.TenantSettings.SingleAsync(s => s.OrganizationId == seed.OrganizationId))
            .DefaultAccountsReceivableId!.Value;
        var voucher = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(seed.OrganizationId, Day, null,
                [
                    new JournalVoucherLineInput(cash.Id, 600m, 0m),
                    new JournalVoucherLineInput(receivable, 0m, 600m, seed.CustomerId),
                ]),
            CancellationToken.None);
        await new ApproveJournalVoucherCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule(),
                new GlCashBalancePolicy(db))
            .Handle(new ApproveJournalVoucherCommand(seed.OrganizationId, voucher.Id), CancellationToken.None);
        var line = await db.JournalVoucherLines.SingleAsync(x => x.JournalVoucherId == voucher.Id && x.ContactId != null);
        await new ApplyPaymentAllocationCommandHandler(db).Handle(
            new ApplyPaymentAllocationCommand(
                seed.OrganizationId, DocumentType.JournalVoucher, line.Id, voucher.Id, DocumentType.Invoice,
                settledByVoucher.Id, 600m),
            CancellationToken.None);

        // Another customer's open invoice.
        var other = await new CreateContactCommandHandler(db, seed.NumberGenerator).Handle(
            new CreateContactCommand(seed.OrganizationId, ContactType.Customer, "Other Traders", null, null, null, null, null, 0m),
            CancellationToken.None);
        var othersInvoice = await InventoryReportSeed.SellAsync(db, seed with { CustomerId = other.Id }, Day, 1m, 150m);

        // A dollar invoice of this customer's.
        var draft = await new CreateInvoiceCommandHandler(db, await SalesPricing.PricerAsync(db, seed.OrganizationId)).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, Day, null,
                [new InvoiceLineInput(seed.ProductId, 1m, 100m, VatRate.NoVat)])
            {
                CurrencyCode = "USD", ExchangeRate = 130m,
            },
            CancellationToken.None);
        var dollar = await InventoryReportSeed.ApproveInvoiceAsync(db, seed, draft.Id, overrideWarning: true);

        return new World(seed, paid.Id, partPaid.Id, settledByVoucher.Id, othersInvoice.Id, dollar.Id);
    }

    private static async Task PayAsync(
        IAppDbContext db, InventoryReportSeed.Seed seed, Account cash, decimal amount, bool approve,
        params PaymentAllocationInput[] allocations)
    {
        var created = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                seed.OrganizationId, seed.CustomerId, PaymentDirection.Received, Day, null, cash.Id, amount, null, allocations),
            CancellationToken.None);
        if (!approve)
        {
            return;
        }

        await new ApprovePaymentCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PaymentPostingRule(),
                new GlCashBalancePolicy(db))
            .Handle(new ApprovePaymentCommand(seed.OrganizationId, created.Id, OverrideNegativeCashBalanceWarning: true),
                CancellationToken.None);
    }
}
