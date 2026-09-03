using ErpApp.Domain.Contacts;
using ErpApp.Domain.Workflow;

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

        var comment = Comment.Create(
            organizationId, CommentParentType.Contact, contactId, "Called about renewal", authorId);

        Assert.Equal(organizationId, comment.OrganizationId);
        Assert.Equal(CommentParentType.Contact, comment.ParentType);
        Assert.Equal(contactId, comment.ParentId);
        Assert.Equal("Called about renewal", comment.Content);
        Assert.Equal(authorId, comment.AuthorUserId);
        Assert.InRange(comment.CreatedAt, before, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Phase 27a: the reason Comment became polymorphic at all -- every transactional detail page's
    /// Activity tab carries a comment composer. A document comment is the same row shape as a
    /// Contact one, distinguished only by ParentType.
    /// </summary>
    [Fact]
    public void Create_accepts_a_document_parent()
    {
        var organizationId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();

        var comment = Comment.Create(
            organizationId, CommentParentType.Invoice, invoiceId, "Chased for payment", Guid.NewGuid());

        Assert.Equal(CommentParentType.Invoice, comment.ParentType);
        Assert.Equal(invoiceId, comment.ParentId);
    }
}
