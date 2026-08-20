using ErpApp.Domain.Accounting;

namespace ErpApp.Domain.UnitTests.Accounting;

public class AccountTests
{
    [Fact]
    public void Create_starts_active_with_given_fields()
    {
        var organizationId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var account = Account.Create(organizationId, "ACC-0001", "Cash in Hand", AccountRootType.Asset, groupId);

        Assert.Equal(organizationId, account.OrganizationId);
        Assert.Equal("ACC-0001", account.Code);
        Assert.Equal("Cash in Hand", account.Name);
        Assert.Equal(AccountRootType.Asset, account.RootType);
        Assert.Equal(groupId, account.GroupId);
        Assert.True(account.IsActive);
        Assert.Equal(AccountKind.Other, account.Kind);
        Assert.Null(account.BankId);
    }

    [Fact]
    public void Update_replaces_editable_fields_but_not_code()
    {
        var account = Account.Create(Guid.NewGuid(), "ACC-0001", "Cash in Hand", AccountRootType.Asset, Guid.NewGuid());
        var newGroupId = Guid.NewGuid();

        account.Update("Petty Cash", newGroupId, AccountRootType.Asset, false, AccountKind.Other, null, null);

        Assert.Equal("Petty Cash", account.Name);
        Assert.Equal(newGroupId, account.GroupId);
        Assert.False(account.IsActive);
        Assert.Equal("ACC-0001", account.Code);
    }

    [Fact]
    public void Create_keeps_bank_id_only_when_kind_is_bank()
    {
        var bankId = Guid.NewGuid();

        var bankAccount = Account.Create(
            Guid.NewGuid(), "BA0001", "NIC Asia", AccountRootType.Asset, Guid.NewGuid(), AccountKind.Bank, bankId);
        var cashAccount = Account.Create(
            Guid.NewGuid(), "BC0001", "Cash in Hand", AccountRootType.Asset, Guid.NewGuid(), AccountKind.Cash, bankId);

        Assert.Equal(bankId, bankAccount.BankId);
        Assert.Null(cashAccount.BankId);
    }

    [Fact]
    public void Update_clears_bank_id_when_kind_changes_away_from_bank()
    {
        var bankId = Guid.NewGuid();
        var account = Account.Create(
            Guid.NewGuid(), "BA0001", "NIC Asia", AccountRootType.Asset, Guid.NewGuid(), AccountKind.Bank, bankId);

        account.Update("NIC Asia", account.GroupId, AccountRootType.Asset, true, AccountKind.Cash, bankId, null);

        Assert.Null(account.BankId);
    }
}
