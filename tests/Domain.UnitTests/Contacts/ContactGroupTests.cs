using ErpApp.Domain.Contacts;

namespace ErpApp.Domain.UnitTests.Contacts;

public class ContactGroupTests
{
    [Fact]
    public void Create_starts_active_with_given_name_and_parent()
    {
        var organizationId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        var group = ContactGroup.Create(organizationId, "Wholesale", parentId);

        Assert.Equal(organizationId, group.OrganizationId);
        Assert.Equal("Wholesale", group.Name);
        Assert.Equal(parentId, group.ParentGroupId);
        Assert.True(group.IsActive);
    }

    [Fact]
    public void Create_allows_no_parent_for_a_root_group()
    {
        var group = ContactGroup.Create(Guid.NewGuid(), "Retail", null);

        Assert.Null(group.ParentGroupId);
    }

    [Fact]
    public void Update_replaces_name_parent_and_active_flag()
    {
        var group = ContactGroup.Create(Guid.NewGuid(), "Wholesale", null);
        var newParentId = Guid.NewGuid();

        group.Update("Wholesale - Renamed", newParentId, false);

        Assert.Equal("Wholesale - Renamed", group.Name);
        Assert.Equal(newParentId, group.ParentGroupId);
        Assert.False(group.IsActive);
    }
}
