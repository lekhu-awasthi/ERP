using ErpApp.Domain.Crm;

namespace ErpApp.Domain.UnitTests.Crm;

public class SmsLogTests
{
    [Fact]
    public void Create_sets_given_fields()
    {
        var organizationId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var senderId = Guid.NewGuid();

        var log = SmsLog.Create(
            organizationId, batchId, contactId, templateId, "Balance Reminder", "Hi Ram, your balance is 500.00.",
            "9800000000", creditsUsed: 1, senderId);

        Assert.Equal(organizationId, log.OrganizationId);
        Assert.Equal(batchId, log.BatchId);
        Assert.Equal(contactId, log.ContactId);
        Assert.Equal(templateId, log.TemplateId);
        Assert.Equal("Balance Reminder", log.Title);
        Assert.Equal("Hi Ram, your balance is 500.00.", log.Content);
        Assert.Equal("9800000000", log.PhoneNumber);
        Assert.Equal(1, log.CreditsUsed);
        Assert.Equal(senderId, log.SentByUserId);
    }

    [Fact]
    public void Create_allows_a_null_template_id_for_a_freeform_send()
    {
        var log = SmsLog.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Ad hoc", "Hello", "9800000000", 1, Guid.NewGuid());

        Assert.Null(log.TemplateId);
    }
}
