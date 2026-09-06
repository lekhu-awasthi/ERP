using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.CreditControl;

/// <summary>
/// Phase 31 -- <c>TenantSettings.CreditLimitExceedsAction</c> against
/// <c>Contact.CreditLimit</c>, enforced at Invoice Approve. Confirmed live 2026-09-06: saving a
/// breaching draft raises nothing, and Approve raises a "Crossed Credit Limit" dialog whose figure
/// is the projected balance, not the excess.
/// </summary>
public class CreditLimitEnforcementTests
{
    [Fact]
    public async Task A_credit_limit_of_zero_means_no_limit_and_never_blocks()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db, creditLimit: 0m, creditLimitExceedsAction: BalanceAction.Reject);
        var invoiceId = await DraftAsync(db, seed, 1_000_000m);

        var result = await ApproveAsync(db, seed, invoiceId);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
    }

    [Fact]
    public async Task Reject_blocks_the_approval_with_a_conflict_naming_the_projected_balance()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(
            db, creditLimit: 100m, creditLimitExceedsAction: BalanceAction.Reject);
        var invoiceId = await DraftAsync(db, seed, 5_000m);

        var error = await Assert.ThrowsAsync<ConflictException>(() => ApproveAsync(db, seed, invoiceId));

        Assert.Contains("5000", error.Message, StringComparison.Ordinal);
        Assert.Contains("100", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The Warn branch is confirmable, and the second flag is what confirms it -- not the
    /// stock override, which is a different dialog for a different reason.</summary>
    [Fact]
    public async Task Warn_is_confirmable_and_only_the_credit_limit_override_confirms_it()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(
            db, creditLimit: 100m, creditLimitExceedsAction: BalanceAction.Warn);
        var invoiceId = await DraftAsync(db, seed, 5_000m);

        await Assert.ThrowsAsync<CreditLimitWarningException>(() => ApproveAsync(db, seed, invoiceId));
        await Assert.ThrowsAsync<CreditLimitWarningException>(
            () => ApproveAsync(db, seed, invoiceId, overrideStock: true));

        var result = await ApproveAsync(db, seed, invoiceId, overrideCreditLimit: true);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
    }

    [Fact]
    public async Task DoNothing_approves_silently()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(
            db, creditLimit: 100m, creditLimitExceedsAction: BalanceAction.DoNothing);
        var invoiceId = await DraftAsync(db, seed, 5_000m);

        var result = await ApproveAsync(db, seed, invoiceId);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
    }

    /// <summary>
    /// The number the live dialog shows is opening balance + approved ledger + this document, which
    /// is why a customer already at 400 against a limit of 1,000 trips on a 700 invoice even though
    /// neither figure exceeds the limit alone.
    /// </summary>
    [Fact]
    public async Task The_projected_balance_is_the_running_ledger_plus_this_document()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(
            db, creditLimit: 1_000m, creditLimitExceedsAction: BalanceAction.Reject, openingBalance: 400m);

        var withinLimit = await DraftAsync(db, seed, 500m);
        await ApproveAsync(db, seed, withinLimit);

        var overLimit = await DraftAsync(db, seed, 200m);
        var error = await Assert.ThrowsAsync<ConflictException>(() => ApproveAsync(db, seed, overLimit));

        Assert.Contains("1100", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tenant setting's own wording is "when a <i>Customer's</i> balance is about to exceed it's
    /// credit limit". A supplier's limit is carried because the live form carries it, and read by
    /// nothing -- pinned so the field is not later mistaken for an unbuilt feature.
    /// </summary>
    [Fact]
    public async Task A_suppliers_credit_limit_is_stored_but_never_enforced()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db, creditLimitExceedsAction: BalanceAction.Reject);
        var supplier = await new ErpApp.Application.Contacts.Commands.CreateContact.CreateContactCommandHandler(
                db, seed.NumberGenerator)
            .Handle(
                new ErpApp.Application.Contacts.Commands.CreateContact.CreateContactCommand(
                    seed.OrganizationId, ContactType.Supplier, "Global Supplies", null, null, null, null, null, 0m,
                    CreditLimit: 1m),
                CancellationToken.None);

        var result = await new ContactCreditLimitPolicy(db).CheckAsync(
            seed.OrganizationId, supplier.Id, 999_999m, CancellationToken.None);

        Assert.Equal(CreditLimitStatus.Ok, result.Status);
    }

    /// <summary>A Lead has no credit block on the live form at all, so the aggregate refuses to
    /// carry one even when a caller sends it.</summary>
    [Fact]
    public async Task A_lead_never_carries_a_credit_limit_or_a_credit_term()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db);
        var lead = await new ErpApp.Application.Contacts.Commands.CreateContact.CreateContactCommandHandler(
                db, seed.NumberGenerator)
            .Handle(
                new ErpApp.Application.Contacts.Commands.CreateContact.CreateContactCommand(
                    seed.OrganizationId, ContactType.Lead, "A Prospect", null, null, null, null, null, 0m,
                    CreditLimit: 5_000m, AcceptsReverseTransactions: true),
                CancellationToken.None);

        var stored = db.Contacts.Single(x => x.Id == lead.Id);

        Assert.Equal(0m, stored.CreditLimit);
        Assert.Null(stored.CreditTermId);
        Assert.False(stored.AcceptsReverseTransactions);
    }

    private static async Task<Guid> DraftAsync(IAppDbContext db, CreditSeed seed, decimal amount)
    {
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, new DateOnly(2026, 1, 10), null,
                [new InvoiceLineInput(seed.ProductId, 1m, amount, VatRate.NoVat)]),
            CancellationToken.None);
        return created.Id;
    }

    private static Task<ApproveInvoiceResult> ApproveAsync(
        IAppDbContext db, CreditSeed seed, Guid invoiceId,
        bool overrideStock = false, bool overrideCreditLimit = false)
    {
        var stockLedgerService = new StockLedgerService(db);
        return new ApproveInvoiceCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
                new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService,
                new ContactCreditLimitPolicy(db))
            .Handle(
                new ApproveInvoiceCommand(seed.OrganizationId, invoiceId, overrideStock, overrideCreditLimit),
                CancellationToken.None);
    }
}
