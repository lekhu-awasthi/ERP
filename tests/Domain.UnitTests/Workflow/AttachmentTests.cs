using ErpApp.Domain.Workflow;

namespace ErpApp.Domain.UnitTests.Workflow;

public class AttachmentTests
{
    [Fact]
    public void Create_sets_given_fields()
    {
        var organizationId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var uploaderId = Guid.NewGuid();

        var attachment = Attachment.Create(
            organizationId, AttachmentParentType.Contact, parentId, "invoice.pdf", 12345, "application/pdf",
            "abc123.pdf", uploaderId);

        Assert.Equal(organizationId, attachment.OrganizationId);
        Assert.Equal(AttachmentParentType.Contact, attachment.ParentType);
        Assert.Equal(parentId, attachment.ParentId);
        Assert.Equal("invoice.pdf", attachment.FileName);
        Assert.Equal(12345, attachment.SizeBytes);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal("abc123.pdf", attachment.StorageKey);
        Assert.Equal(uploaderId, attachment.UploadedByUserId);
    }

    [Fact]
    public void Create_timestamps_now()
    {
        var before = DateTimeOffset.UtcNow;

        var attachment = Attachment.Create(
            Guid.NewGuid(), AttachmentParentType.Contact, Guid.NewGuid(), "a.png", 100, "image/png", "key", Guid.NewGuid());

        Assert.InRange(attachment.UploadedAt, before, DateTimeOffset.UtcNow);
    }
}
