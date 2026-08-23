using ErpApp.Domain.Crm;

namespace ErpApp.Domain.UnitTests.Crm;

public class SmsTemplateTests
{
    [Fact]
    public void Create_sets_given_fields()
    {
        var organizationId = Guid.NewGuid();

        var template = SmsTemplate.Create(organizationId, "Balance Reminder", "Hi $[name]$, your balance is $[balance]$.");

        Assert.Equal(organizationId, template.OrganizationId);
        Assert.Equal("Balance Reminder", template.Title);
        Assert.Equal("Hi $[name]$, your balance is $[balance]$.", template.Content);
    }

    [Fact]
    public void Update_replaces_title_and_content()
    {
        var template = SmsTemplate.Create(Guid.NewGuid(), "Old Title", "Old content");

        template.Update("New Title", "New content $[name]$");

        Assert.Equal("New Title", template.Title);
        Assert.Equal("New content $[name]$", template.Content);
    }
}
