using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>Shared "new Organization with a Cash (Asset) and Sales Revenue (Income) account"
/// fixture reused by every Accounting handler test that needs two real Accounts to reference.</summary>
public static class AccountingTestSeed
{
    public static async Task<(Guid OrganizationId, Guid CashAccountId, Guid SalesAccountId)> SeedTwoAccountsAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var incomeGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);

        var cash = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", assetGroup.Id), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id), CancellationToken.None);

        return (organizationId, cash.Id, sales.Id);
    }
}
