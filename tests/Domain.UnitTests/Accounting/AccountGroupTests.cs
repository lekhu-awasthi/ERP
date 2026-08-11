using ErpApp.Domain.Accounting;

namespace ErpApp.Domain.UnitTests.Accounting;

public class AccountGroupTests
{
    [Fact]
    public void Create_starts_active_with_given_name_root_type_and_parent()
    {
        var organizationId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        var group = AccountGroup.Create(organizationId, "Current Assets", AccountRootType.Asset, parentId);

        Assert.Equal(organizationId, group.OrganizationId);
        Assert.Equal("Current Assets", group.Name);
        Assert.Equal(AccountRootType.Asset, group.RootType);
        Assert.Equal(parentId, group.ParentGroupId);
        Assert.True(group.IsActive);
    }

    [Fact]
    public void Update_replaces_name_parent_and_active_flag_but_not_root_type()
    {
        var group = AccountGroup.Create(Guid.NewGuid(), "Current Assets", AccountRootType.Asset, null);
        var newParentId = Guid.NewGuid();

        group.Update("Fixed Assets", newParentId, false);

        Assert.Equal("Fixed Assets", group.Name);
        Assert.Equal(newParentId, group.ParentGroupId);
        Assert.False(group.IsActive);
        Assert.Equal(AccountRootType.Asset, group.RootType);
    }
}
