using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.UnitTests.Catalog;

public class ProductCategoryTests
{
    [Fact]
    public void Create_starts_active_with_given_name_and_parent()
    {
        var organizationId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        var category = ProductCategory.Create(organizationId, "Electronics", parentId);

        Assert.Equal(organizationId, category.OrganizationId);
        Assert.Equal("Electronics", category.Name);
        Assert.Equal(parentId, category.ParentCategoryId);
        Assert.True(category.IsActive);
    }

    [Fact]
    public void Update_replaces_name_parent_and_active_flag()
    {
        var category = ProductCategory.Create(Guid.NewGuid(), "Electronics", null);
        var newParentId = Guid.NewGuid();

        category.Update("Electronics - Renamed", newParentId, false);

        Assert.Equal("Electronics - Renamed", category.Name);
        Assert.Equal(newParentId, category.ParentCategoryId);
        Assert.False(category.IsActive);
    }
}
