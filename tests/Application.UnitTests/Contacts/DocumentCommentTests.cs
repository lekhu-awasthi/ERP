using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Contacts.Commands.AddComment;
using ErpApp.Application.Contacts.Queries.ListComments;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Sales.Commands.CreateQuotation;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Workflow;

namespace ErpApp.Application.UnitTests.Contacts;

/// <summary>
/// Phase 27a -- Comment became polymorphic here, on the trigger phase-18 decision #3 set for it
/// ("generalize only if/when a second parent type is actually needed"). Every transactional detail
/// page's Activity tab carries a real comment composer, live-confirmed, so it is needed.
///
/// <para>These tests pin the two things that would break silently: a document comment and a Contact
/// comment must not see each other, and the permission key must come from the parent rather than
/// staying hardcoded to ContactManage -- otherwise a Member who may edit invoices but holds no
/// Contact grant could not comment on one.</para>
/// </summary>
public class DocumentCommentTests
{
    [Fact]
    public async Task A_comment_can_be_filed_against_a_document_and_reads_back_under_that_parent()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var authorId = await CreateUserAsync(db);
        var quotationId = await CreateQuotationAsync(db, organizationId);

        await new AddCommentCommandHandler(db, new FakeCurrentUserService(authorId)).Handle(
            new AddCommentCommand(organizationId, CommentParentType.Quotation, quotationId, "Customer wants a revision"),
            CancellationToken.None);

        var listed = await new ListCommentsQueryHandler(db).Handle(
            new ListCommentsQuery(organizationId, CommentParentType.Quotation, quotationId), CancellationToken.None);

        Assert.Equal(1, listed.TotalCount);
        Assert.Equal("Customer wants a revision", listed.Rows[0].Content);
    }

    [Fact]
    public async Task Comments_on_a_contact_and_on_a_document_do_not_leak_into_each_other()
    {
        // The one that would go unnoticed: before Phase 27a the filter was a bare ContactId, and a
        // ParentId-only filter would still have matched -- a document whose id happened to equal a
        // contact's would be an impossible coincidence, but a filter that forgot ParentType would
        // show every parent's comments the moment two parents shared an id space. They do not, so
        // this asserts the discriminator is actually in the Where clause.
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var authorId = await CreateUserAsync(db);
        var contactId = await CreateContactAsync(db, organizationId);
        var quotationId = await CreateQuotationAsync(db, organizationId);

        var handler = new AddCommentCommandHandler(db, new FakeCurrentUserService(authorId));
        await handler.Handle(
            new AddCommentCommand(organizationId, CommentParentType.Contact, contactId, "Called the customer"),
            CancellationToken.None);
        await handler.Handle(
            new AddCommentCommand(organizationId, CommentParentType.Quotation, quotationId, "Quoted at list price"),
            CancellationToken.None);

        var query = new ListCommentsQueryHandler(db);

        var contactComments = await query.Handle(
            new ListCommentsQuery(organizationId, CommentParentType.Contact, contactId), CancellationToken.None);
        var quotationComments = await query.Handle(
            new ListCommentsQuery(organizationId, CommentParentType.Quotation, quotationId), CancellationToken.None);

        Assert.Equal("Called the customer", Assert.Single(contactComments.Rows).Content);
        Assert.Equal("Quoted at list price", Assert.Single(quotationComments.Rows).Content);
    }

    [Fact]
    public async Task A_comment_cannot_be_filed_against_a_document_that_does_not_exist()
    {
        var db = TestAppDbContext.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new AddCommentCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
                new AddCommentCommand(Guid.NewGuid(), CommentParentType.Invoice, Guid.NewGuid(), "orphan"),
                CancellationToken.None));
    }

    [Fact]
    public void The_permission_key_comes_from_the_parent_not_from_Contact()
    {
        Assert.Equal(
            PermissionKeys.InvoiceEdit,
            new AddCommentCommand(Guid.NewGuid(), CommentParentType.Invoice, Guid.NewGuid(), "x").PermissionKey);
        Assert.Equal(
            PermissionKeys.ProductionJournalView,
            new ListCommentsQuery(Guid.NewGuid(), CommentParentType.ProductionJournal, Guid.NewGuid()).PermissionKey);

        // Contact keeps its own pre-split pair -- Contacts have never had an Edit key.
        Assert.Equal(
            PermissionKeys.ContactManage,
            new AddCommentCommand(Guid.NewGuid(), CommentParentType.Contact, Guid.NewGuid(), "x").PermissionKey);
    }

    private static async Task<Guid> CreateUserAsync(IAppDbContext db)
    {
        var user = User.Register("Author", $"{Guid.NewGuid():N}@example.com", "9800000000", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private static async Task<Guid> CreateContactAsync(IAppDbContext db, Guid organizationId)
    {
        var contact = await new CreateContactCommandHandler(db, new FakeDocumentNumberGenerator()).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Retail", null, null, null, null, null, 0m),
            CancellationToken.None);
        return contact.Id;
    }

    private static async Task<Guid> CreateQuotationAsync(IAppDbContext db, Guid organizationId)
    {
        var customerId = await CreateContactAsync(db, organizationId);
        var quotation = await new CreateQuotationCommandHandler(db).Handle(
            new CreateQuotationCommand(organizationId, customerId, new DateOnly(2026, 1, 1), null, null, []),
            CancellationToken.None);
        return quotation.Id;
    }
}
