using ErpApp.Application.Accounting.Cash;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.CreditControl;

/// <summary>Phase 31 -- <c>TenantSettings.NegativeCashBalanceAction</c>, dead since phase 2.</summary>
public class GlCashBalancePolicyTests
{
    [Theory]
    [InlineData(BalanceAction.Reject, CashBalanceStatus.Reject)]
    [InlineData(BalanceAction.Warn, CashBalanceStatus.Warn)]
    [InlineData(BalanceAction.DoNothing, CashBalanceStatus.Ok)]
    public async Task An_overdrawing_outflow_takes_the_action_the_tenant_chose(
        BalanceAction setting, CashBalanceStatus expected)
    {
        var db = TestAppDbContext.Create();
        var (organizationId, bankAccountId) = await SeedAsync(db, setting, openingDebit: 100m);

        var result = await new GlCashBalancePolicy(db).CheckAsync(
            organizationId, [new CashOutflow(bankAccountId, 250m)], CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Equal(-150m, result.ProjectedBalance);
    }

    [Fact]
    public async Task An_outflow_within_the_balance_is_ok_and_never_reads_the_setting()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, bankAccountId) = await SeedAsync(db, BalanceAction.Reject, openingDebit: 1_000m);

        var result = await new GlCashBalancePolicy(db).CheckAsync(
            organizationId, [new CashOutflow(bankAccountId, 250m)], CancellationToken.None);

        Assert.Equal(CashBalanceStatus.Ok, result.Status);
    }

    /// <summary>
    /// The setting is about "cash and bank balance". An ordinary asset or expense account going into
    /// credit is somebody else's business, so a non-Bank/Cash account is dropped before any balance
    /// is even read -- which is also what keeps every pre-phase-31 test passing unchanged, since
    /// nothing else in the suite creates a Bank-kind account.
    /// </summary>
    [Fact]
    public async Task An_account_that_is_not_Bank_or_Cash_kind_is_never_checked()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, _) = await SeedAsync(db, BalanceAction.Reject, openingDebit: 0m);
        var ordinary = await new CreateAccountCommandHandler(db, new FakeDocumentNumberGenerator()).Handle(
            new CreateAccountCommand(organizationId, "Sundry Asset", AssetGroupId(db, organizationId)),
            CancellationToken.None);

        var result = await new GlCashBalancePolicy(db).CheckAsync(
            organizationId, [new CashOutflow(ordinary.Id, 9_999m)], CancellationToken.None);

        Assert.Equal(CashBalanceStatus.Ok, result.Status);
    }

    /// <summary>A voucher line pair that debits and credits the same bank account nets to nothing;
    /// the handler sums per account before calling in, and this pins that a zero (or negative) net
    /// is not read as a withdrawal.</summary>
    [Fact]
    public async Task A_zero_or_negative_net_outflow_is_ignored()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, bankAccountId) = await SeedAsync(db, BalanceAction.Reject, openingDebit: 0m);

        var result = await new GlCashBalancePolicy(db).CheckAsync(
            organizationId, [new CashOutflow(bankAccountId, -500m)], CancellationToken.None);

        Assert.Equal(CashBalanceStatus.Ok, result.Status);
    }

    private static Guid AssetGroupId(IAppDbContext db, Guid organizationId) =>
        db.AccountGroups.First(x => x.OrganizationId == organizationId && x.RootType == AccountRootType.Asset).Id;

    private static async Task<(Guid OrganizationId, Guid BankAccountId)> SeedAsync(
        IAppDbContext db, BalanceAction action, decimal openingDebit)
    {
        var organizationId = Guid.NewGuid();
        var numbers = new FakeDocumentNumberGenerator();

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null),
            CancellationToken.None);
        var equityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Capital", AccountRootType.Equity, null),
            CancellationToken.None);

        var bank = await new CreateAccountCommandHandler(db, numbers).Handle(
            new CreateAccountCommand(organizationId, "Everest Bank", assetGroup.Id, AccountKind.Bank),
            CancellationToken.None);
        var capital = await new CreateAccountCommandHandler(db, numbers).Handle(
            new CreateAccountCommand(organizationId, "Owner Capital", equityGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.UpdateSettings(
            SuggestSellingPriceMode.RecentSellingPrice, ProductPriceBasis.ExclusiveOfVat,
            InventoryTrackingMode.AccountingMovement, action, BalanceAction.Warn, BalanceAction.Warn);
        db.TenantSettings.Add(settings);

        if (openingDebit > 0m)
        {
            db.GlJournalEntries.Add(GlJournalEntry.Post(
                organizationId, DocumentType.JournalVoucher, Guid.NewGuid(),
                [new GlLineInput(bank.Id, openingDebit, 0m), new GlLineInput(capital.Id, 0m, openingDebit)]));
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return (organizationId, bank.Id);
    }
}
