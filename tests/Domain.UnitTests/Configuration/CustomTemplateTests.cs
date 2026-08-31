using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

public class CustomTemplateTests
{
    [Fact]
    public void Create_starts_active_with_the_given_default_flag()
    {
        var template = CustomTemplate.Create(
            Guid.NewGuid(), "Standard Letter", CustomTemplateType.CustomerBalanceConfirmation, "Dear $[CustomerName]$,", isDefault: true);

        Assert.Equal("Standard Letter", template.Name);
        Assert.Equal(CustomTemplateType.CustomerBalanceConfirmation, template.Type);
        Assert.Equal("Dear $[CustomerName]$,", template.Body);
        Assert.True(template.IsDefault);
        Assert.True(template.IsActive);
    }

    [Fact]
    public void Update_replaces_name_type_body_and_active_flag()
    {
        var template = CustomTemplate.Create(
            Guid.NewGuid(), "Standard Letter", CustomTemplateType.CustomerBalanceConfirmation, "Dear $[CustomerName]$,", isDefault: true);

        template.Update("Formal Letter", CustomTemplateType.CustomerBalanceConfirmation, "Dear Sir/Madam $[CustomerName]$,", false);

        Assert.Equal("Formal Letter", template.Name);
        Assert.Equal("Dear Sir/Madam $[CustomerName]$,", template.Body);
        Assert.False(template.IsActive);
    }

    [Fact]
    public void Update_clears_default_when_moved_to_a_different_type()
    {
        var template = CustomTemplate.Create(
            Guid.NewGuid(), "Standard Letter", CustomTemplateType.CustomerBalanceConfirmation, "Dear $[CustomerName]$,", isDefault: true);

        template.Update("Standard Letter", CustomTemplateType.SupplierBalanceConfirmation, "Dear $[SupplierName]$,", true);

        Assert.False(template.IsDefault);
    }

    [Fact]
    public void MarkAsDefault_and_ClearDefault_toggle_the_flag()
    {
        var template = CustomTemplate.Create(
            Guid.NewGuid(), "Standard Letter", CustomTemplateType.Email, "Hello $[ContactName]$,", isDefault: false);

        template.MarkAsDefault();
        Assert.True(template.IsDefault);

        template.ClearDefault();
        Assert.False(template.IsDefault);
    }
}
