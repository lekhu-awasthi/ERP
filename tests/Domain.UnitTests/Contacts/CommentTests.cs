using ErpApp.Domain.Contacts;

namespace ErpApp.Domain.UnitTests.Contacts;

public class CommentTests
{
    [Fact]
    public void Create_sets_given_fields_and_timestamps_now()
    {
        var organizationId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        var comment = Comment.Create(organizationId, contactId, "Called about renewal", authorId);

        Assert.Equal(organizationId, comment.OrganizationId);
        Assert.Equal(contactId, comment.ContactId);
        Assert.Equal("Called about renewal", comment.Content);
        Assert.Equal(authorId, comment.AuthorUserId);
        Assert.InRange(comment.CreatedAt, before, DateTimeOffset.UtcNow);
    }
}
