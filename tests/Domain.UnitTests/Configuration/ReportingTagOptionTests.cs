using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

public class ReportingTagOptionTests
{
    [Fact]
    public void Create_starts_active_scoped_to_given_category()
    {
        var categoryId = Guid.NewGuid();

        var option = ReportingTagOption.Create(Guid.NewGuid(), "Project A", categoryId);

        Assert.Equal("Project A", option.Name);
        Assert.Equal(categoryId, option.CategoryId);
        Assert.True(option.IsActive);
    }

    [Fact]
    public void Update_replaces_name_category_and_active_flag()
    {
        var option = ReportingTagOption.Create(Guid.NewGuid(), "Project A", Guid.NewGuid());
        var newCategoryId = Guid.NewGuid();

        option.Update("Project B", newCategoryId, false);

        Assert.Equal("Project B", option.Name);
        Assert.Equal(newCategoryId, option.CategoryId);
        Assert.False(option.IsActive);
    }
}
