using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Accounting.Queries.CashFlowSummary;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Payments;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Posting;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Accounting;

public class CashFlowSummaryQueryHandlerTests
{
    /// <summary>Phase 19 decision #2 -- direct-method Bank/Cash movement summary. GlJournalEntry.
    /// PostedAt is stamped from the real clock at Approve() time (see GlDateBoundary's own doc
    /// comment), not a document's own business Date -- same constraint every existing GL-report
    /// test (TrialBalance/BalanceSheet/IncomeStatement) already works within, so the query window
    /// brackets "now" (DateOnly.FromDateTime(DateTime.UtcNow)) rather than fixed calendar dates. A
    /// Customer Payment and a Supplier Payment land in their own named buckets; a JournalVoucher
    /// touching Cash directly (no Payment involved) lands in Other Receipts/Other Payments by its
    /// own Debit/Credit side. Exit criterion #3's reconciliation is exercised structurally --
    /// Ending Balance must equal Starting Balance plus every bucket's net movement.</summary>
    [Fact]
    public async Task Handle_buckets_cash_movements_by_document_classification_and_reconciles_starting_to_ending_balance()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await CreateAndApprovePaymentAsync(db, seed, today, PaymentDirection.Received, seed.CustomerId, 500m);
        await CreateAndApprovePaymentAsync(db, seed, today, PaymentDirection.Paid, seed.SupplierId, 200m);
        await CreateAndApproveJournalVoucherAsync(db, seed, today, debitCash: true, 300m); // Other Receipts
        await CreateAndApproveJournalVoucherAsync(db, seed, today, debitCash: false, 150m); // Other Payments

        var handler = new CashFlowSummaryQueryHandler(db);
        var result = await handler.Handle(
            new CashFlowSummaryQuery(seed.OrganizationId, today, today, null),
            CancellationToken.None);

        Assert.Equal(0m, result.StartingBalance);
        Assert.Equal(500m, result.ReceivedFromCustomerCashIn);
        Assert.Equal(0m, result.ReceivedFromCustomerCashOut);
        Assert.Equal(200m, result.PaidToSupplierCashOut);
        Assert.Equal(0m, result.PaidToSupplierCashIn);
        Assert.Equal(300m, result.OtherReceiptsCashIn);
        Assert.Equal(150m, result.OtherPaymentsCashOut);

        // 500 (customer) - 200 (supplier) + 300 (other receipts) - 150 (other payments)
        Assert.Equal(450m, result.EndingBalance);
    }

    /// <summary>Any activity posted before the query window's FromDate rolls up into a single
    /// Starting Balance figure, regardless of which bucket it would otherwise classify into.</summary>
    [Fact]
    public async Task Handle_rolls_up_all_prior_activity_into_starting_balance()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await CreateAndApprovePaymentAsync(db, seed, today, PaymentDirection.Received, seed.CustomerId, 1000m);

        var handler = new CashFlowSummaryQueryHandler(db);
        var result = await handler.Handle(
            new CashFlowSummaryQuery(seed.OrganizationId, today.AddDays(1), today.AddDays(30), null),
            CancellationToken.None);

        Assert.Equal(1000m, result.StartingBalance);
        Assert.Equal(1000m, result.EndingBalance);
        Assert.Equal(0m, result.ReceivedFromCustomerCashIn);
    }

    [Fact]
    public async Task Handle_narrows_to_a_single_bank_account_when_one_is_specified()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var secondCashAccount = await new CreateAccountCommandHandler(db, seed.NumberGenerator).Handle(
            new CreateAccountCommand(seed.OrganizationId, "Petty Cash", seed.AssetGroupId, AccountKind.Cash), CancellationToken.None);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await CreateAndApprovePaymentAsync(db, seed, today, PaymentDirection.Received, seed.CustomerId, 400m);
        await CreateAndApprovePaymentAsync(
            db, seed, today, PaymentDirection.Received, seed.CustomerId, 900m, secondCashAccount.Id);

        var handler = new CashFlowSummaryQueryHandler(db);
        var result = await handler.Handle(
            new CashFlowSummaryQuery(seed.OrganizationId, today, today, seed.CashAccountId),
            CancellationToken.None);

        Assert.Equal(400m, result.ReceivedFromCustomerCashIn);
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid SupplierId,
        Guid CashAccountId, Guid AssetGroupId, Guid SalesAccountId, Guid PurchaseAccountId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);
        var supplier = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Supplier, "Global Supplies", null, null, null, null, null, 0m),
            CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var incomeGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);
        var expenseGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Operating Expenses", AccountRootType.Expense, null), CancellationToken.None);

        var cash = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", assetGroup.Id, AccountKind.Cash), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id), CancellationToken.None);
        var purchase = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Purchase Expense", expenseGroup.Id), CancellationToken.None);
        var ar = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Receivable", assetGroup.Id), CancellationToken.None);
        var vatPayable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Payable", liabilityGroup.Id), CancellationToken.None);
        var ap = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Payable", liabilityGroup.Id), CancellationToken.None);
        var vatReceivable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Receivable", assetGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, purchase.Id, ap.Id, vatReceivable.Id, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(organizationId, numberGenerator, customer.Id, supplier.Id, cash.Id, assetGroup.Id, sales.Id, purchase.Id);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApprovePaymentAsync(
        IAppDbContext db, Seed seed, DateOnly date, PaymentDirection direction, Guid contactId, decimal amount, Guid? accountId = null)
    {
        var created = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                seed.OrganizationId, contactId, direction, date, null, accountId ?? seed.CashAccountId, amount, null, []),
            CancellationToken.None);

        var approved = await new ApprovePaymentCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PaymentPostingRule())
            .Handle(new ApprovePaymentCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveJournalVoucherAsync(
        IAppDbContext db, Seed seed, DateOnly date, bool debitCash, decimal amount)
    {
        var lines = debitCash
            ? new[]
              {
                  new JournalVoucherLineInput(seed.CashAccountId, amount, 0),
                  new JournalVoucherLineInput(seed.SalesAccountId, 0, amount),
              }
            : new[]
              {
                  new JournalVoucherLineInput(seed.PurchaseAccountId, amount, 0),
                  new JournalVoucherLineInput(seed.CashAccountId, 0, amount),
              };

        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(seed.OrganizationId, date, null, lines), CancellationToken.None);

        var approved = await new ApproveJournalVoucherCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
