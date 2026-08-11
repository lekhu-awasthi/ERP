using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

public class ReportingTagCategoryTests
{
    [Fact]
    public void Create_starts_active_with_given_name()
    {
        var category = ReportingTagCategory.Create(Guid.NewGuid(), "Project");

        Assert.Equal("Project", category.Name);
        Assert.True(category.IsActive);
    }

    [Fact]
    public void Update_replaces_name_and_active_flag()
    {
        var category = ReportingTagCategory.Create(Guid.NewGuid(), "Project");

        category.Update("Department", false);

        Assert.Equal("Department", category.Name);
        Assert.False(category.IsActive);
    }
}
