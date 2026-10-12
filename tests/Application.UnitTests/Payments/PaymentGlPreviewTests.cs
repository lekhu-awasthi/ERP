using ErpApp.Application.Accounting.Cash;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Payments;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Posting;
using ErpApp.Application.Payments.Queries.PreviewPaymentGlPosting;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Payments;

/// <summary>
/// Phase 88 -- the payment form's GL preview is the entry Approve posts. Each test previews a payload, then saves and
/// approves the same payload, and holds the two to the same lines in the same order: phase 87's split of the control
/// leg (every default moved between the invoices), and a foreign receipt's conversion and realised exchange
/// difference, neither of which the preview could show before.
/// </summary>
public sealed class PaymentGlPreviewTests
{
    private static readonly DateOnly Day = new(2026, 10, 12);

    [Fact]
    public async Task The_preview_shows_the_split_approve_posts_when_the_invoices_sit_on_different_receivables()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var cash = await CashAccountAsync(db, seed.OrganizationId);
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m);

        var x = await InventoryReportSeed.SellAsync(db, seed, Day, 4m, 150m, vatRate: VatRate.ThirteenPercentVat);
        await MoveReceivableAsync(db, seed.OrganizationId);
        var y = await InventoryReportSeed.SellAsync(db, seed, Day, 4m, 150m, vatRate: VatRate.ThirteenPercentVat);

        PaymentAllocationInput[] allocations =
        [
            new(DocumentType.Invoice, x.Id, 678m),
            new(DocumentType.Invoice, y.Id, 100m),
        ];

        var preview = await PreviewAsync(db, seed.OrganizationId, cash, 1000m, null, null, allocations);
        var posted = await ApproveAsync(db, seed, cash, 1000m, null, null, allocations);

        Assert.Equal(posted, preview);
        Assert.Equal(3, preview.Count); // cash, the old receivable, the new one
    }

    [Fact]
    public async Task The_preview_of_a_foreign_receipt_converts_its_amount_and_shows_the_realised_difference()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var cash = await CashAccountAsync(db, seed.OrganizationId);
        var gain = await AddForexAccountsAsync(db, seed.OrganizationId);
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m);

        // USD 100 booked at 130 (NPR 13,000), received at 132 (NPR 13,200): a gain of 200.
        var draft = await new CreateInvoiceCommandHandler(db, await SalesPricing.PricerAsync(db, seed.OrganizationId)).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, Day, null,
                [new InvoiceLineInput(seed.ProductId, 1m, 100m, VatRate.NoVat)])
            {
                CurrencyCode = "USD", ExchangeRate = 130m,
            },
            CancellationToken.None);
        var invoice = await InventoryReportSeed.ApproveInvoiceAsync(db, seed, draft.Id, overrideWarning: true);

        PaymentAllocationInput[] allocations = [new(DocumentType.Invoice, invoice.Id, 100m)];

        var preview = await PreviewAsync(db, seed.OrganizationId, cash, 100m, "USD", 132m, allocations);
        var posted = await ApproveAsync(db, seed, cash, 100m, "USD", 132m, allocations);

        Assert.Equal(posted, preview);
        Assert.Contains((cash, 13200m, 0m), preview);
        Assert.Contains((gain, 0m, 200m), preview);
    }

    [Fact]
    public async Task A_payment_on_account_previews_the_one_line_entry_it_always_did()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var cash = await CashAccountAsync(db, seed.OrganizationId);
        var receivable = (await db.TenantSettings.SingleAsync(s => s.OrganizationId == seed.OrganizationId))
            .DefaultAccountsReceivableId!.Value;

        var preview = await PreviewAsync(db, seed.OrganizationId, cash, 500m, null, null, []);

        Assert.Equal([(cash, 500m, 0m), (receivable, 0m, 500m)], preview);
    }

    [Fact]
    public void The_validator_refuses_allocations_beyond_the_amount_and_a_missing_list()
    {
        var validator = new PreviewPaymentGlPostingQueryValidator();
        var org = Guid.NewGuid();
        var account = Guid.NewGuid();

        var over = validator.Validate(new PreviewPaymentGlPostingQuery(
            org, account, 100m, PaymentDirection.Received, null, null,
            [new PaymentAllocationInput(DocumentType.Invoice, Guid.NewGuid(), 150m)]));
        Assert.Contains(over.Errors, e => e.PropertyName == "Allocations");

        var missing = validator.Validate(new PreviewPaymentGlPostingQuery(
            org, account, 100m, PaymentDirection.Received, null, null, null!));
        Assert.Contains(missing.Errors, e => e.PropertyName == "Allocations");

        var foreignAtOne = validator.Validate(new PreviewPaymentGlPostingQuery(
            org, account, 100m, PaymentDirection.Received, "NPR", 2m, []));
        Assert.False(foreignAtOne.IsValid);
    }

    private static async Task<List<(Guid, decimal, decimal)>> PreviewAsync(
        IAppDbContext db, Guid organizationId, Guid cash, decimal amount, string? currency, decimal? rate,
        IReadOnlyList<PaymentAllocationInput> allocations)
    {
        var lines = await new PreviewPaymentGlPostingQueryHandler(db, new PaymentPostingRule()).Handle(
            new PreviewPaymentGlPostingQuery(
                organizationId, cash, amount, PaymentDirection.Received, currency, rate, allocations),
            CancellationToken.None);
        return [.. lines.Select(l => (l.AccountId, l.Debit, l.Credit))];
    }

    private static async Task<List<(Guid, decimal, decimal)>> ApproveAsync(
        IAppDbContext db, InventoryReportSeed.Seed seed, Guid cash, decimal amount, string? currency, decimal? rate,
        IReadOnlyList<PaymentAllocationInput> allocations)
    {
        var created = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                seed.OrganizationId, seed.CustomerId, PaymentDirection.Received, Day, null, cash, amount, null, allocations)
            {
                CurrencyCode = currency, ExchangeRate = rate,
            },
            CancellationToken.None);
        await new ApprovePaymentCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PaymentPostingRule(),
                new GlCashBalancePolicy(db))
            .Handle(new ApprovePaymentCommand(seed.OrganizationId, created.Id, OverrideNegativeCashBalanceWarning: true),
                CancellationToken.None);

        var entry = await db.GlJournalEntries.Include(e => e.Lines)
            .SingleAsync(e => e.SourceDocumentType == DocumentType.Payment && e.SourceDocumentId == created.Id);
        return [.. entry.Lines.Select(l => (l.AccountId, l.Debit, l.Credit))];
    }

    private static async Task<Guid> CashAccountAsync(IAppDbContext db, Guid organizationId)
    {
        var cash = Account.Create(organizationId, "1010", "Cash In Hand", AccountRootType.Asset, Guid.NewGuid(), AccountKind.Cash);
        db.Accounts.Add(cash);
        await db.SaveChangesAsync();
        return cash.Id;
    }

    /// <summary>Today's Receivable moves to a new account; everything else stays.</summary>
    private static async Task MoveReceivableAsync(IAppDbContext db, Guid organizationId)
    {
        var receivable = Account.Create(organizationId, "1201", "Receivable 2083", AccountRootType.Asset, Guid.NewGuid());
        db.Accounts.Add(receivable);
        var s = await db.TenantSettings.SingleAsync(x => x.OrganizationId == organizationId);
        s.SetAccountingDefaults(
            s.DefaultSalesAccountId, receivable.Id, s.DefaultVatPayableAccountId, s.DefaultPurchaseAccountId,
            s.DefaultAccountsPayableId, s.DefaultVatReceivableAccountId, s.DefaultTdsPayableAccountId,
            s.DefaultForexGainAccountId, s.DefaultForexLossAccountId);
        await db.SaveChangesAsync();
    }

    /// <summary>Forex Gain and Forex Loss accounts set as the tenant's defaults; returns the gain account.</summary>
    private static async Task<Guid> AddForexAccountsAsync(IAppDbContext db, Guid organizationId)
    {
        var gain = Account.Create(organizationId, "4901", "Forex Gain", AccountRootType.Income, Guid.NewGuid());
        var loss = Account.Create(organizationId, "5901", "Forex Loss", AccountRootType.Expense, Guid.NewGuid());
        db.Accounts.AddRange(gain, loss);
        var s = await db.TenantSettings.SingleAsync(x => x.OrganizationId == organizationId);
        s.SetAccountingDefaults(
            s.DefaultSalesAccountId, s.DefaultAccountsReceivableId, s.DefaultVatPayableAccountId, s.DefaultPurchaseAccountId,
            s.DefaultAccountsPayableId, s.DefaultVatReceivableAccountId, s.DefaultTdsPayableAccountId, gain.Id, loss.Id);
        await db.SaveChangesAsync();
        return gain.Id;
    }
}
