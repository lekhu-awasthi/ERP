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
    }

    [Fact]
    public void Update_replaces_editable_fields_but_not_code()
    {
        var account = Account.Create(Guid.NewGuid(), "ACC-0001", "Cash in Hand", AccountRootType.Asset, Guid.NewGuid());
        var newGroupId = Guid.NewGuid();

        account.Update("Petty Cash", newGroupId, AccountRootType.Asset, false);

        Assert.Equal("Petty Cash", account.Name);
        Assert.Equal(newGroupId, account.GroupId);
        Assert.False(account.IsActive);
        Assert.Equal("ACC-0001", account.Code);
    }
}
