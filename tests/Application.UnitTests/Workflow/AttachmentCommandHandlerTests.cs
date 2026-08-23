using System.Text;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Application.Workflow.Commands.DeleteAttachment;
using ErpApp.Application.Workflow.Commands.UploadAttachment;
using ErpApp.Application.Workflow.Queries.GetAttachmentForDownload;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Workflow;

/// <summary>
/// Covers roadmap Phase 18's Attachment feature. GetAttachmentForDownload's cross-organization
/// test is the handler-level proof point for exit criteria #8 (download must never leak another
/// organization's file) -- the actual 403/404-over-the-wire proof happens in manual E2E, since
/// permission enforcement itself lives in AuthorizationBehavior, not in the handler.
/// </summary>
public class AttachmentCommandHandlerTests
{
    [Fact]
    public async Task Upload_rejects_a_parent_id_that_does_not_resolve_to_an_existing_contact()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new UploadAttachmentCommandHandler(db, new FakeFileStorage(), new FakeCurrentUserService(Guid.NewGuid()));

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UploadAttachmentCommand(
                organizationId, AttachmentParentType.Contact, Guid.NewGuid(), "a.pdf", 100, "application/pdf",
                new MemoryStream(Encoding.UTF8.GetBytes("data"))),
            CancellationToken.None));
    }

    [Fact]
    public async Task Upload_then_download_round_trips_the_same_bytes_through_file_storage()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var contactId = await CreateContactAsync(db, organizationId);
        var uploaderId = await CreateUserAsync(db);
        var fileStorage = new FakeFileStorage();

        var originalBytes = Encoding.UTF8.GetBytes("hello attachment");
        var uploadHandler = new UploadAttachmentCommandHandler(db, fileStorage, new FakeCurrentUserService(uploaderId));
        var uploaded = await uploadHandler.Handle(
            new UploadAttachmentCommand(
                organizationId, AttachmentParentType.Contact, contactId, "note.txt", originalBytes.Length, "text/plain",
                new MemoryStream(originalBytes)),
            CancellationToken.None);

        var downloadHandler = new GetAttachmentForDownloadQueryHandler(db);
        var metadata = await downloadHandler.Handle(
            new GetAttachmentForDownloadQuery(organizationId, uploaded.Id), CancellationToken.None);

        var stream = await fileStorage.OpenReadAsync(metadata.StorageKey, CancellationToken.None);
        using var reader = new StreamReader(stream);
        var roundTripped = await reader.ReadToEndAsync();

        Assert.Equal("hello attachment", roundTripped);
        Assert.Equal("note.txt", metadata.FileName);
    }

    [Fact]
    public async Task GetAttachmentForDownload_returns_not_found_for_a_different_organization()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        var contactId = await CreateContactAsync(db, organizationId);
        var uploaderId = await CreateUserAsync(db);
        var fileStorage = new FakeFileStorage();

        var uploaded = await new UploadAttachmentCommandHandler(db, fileStorage, new FakeCurrentUserService(uploaderId)).Handle(
            new UploadAttachmentCommand(
                organizationId, AttachmentParentType.Contact, contactId, "a.pdf", 4, "application/pdf",
                new MemoryStream(Encoding.UTF8.GetBytes("data"))),
            CancellationToken.None);

        var handler = new GetAttachmentForDownloadQueryHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new GetAttachmentForDownloadQuery(otherOrganizationId, uploaded.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_removes_both_the_db_row_and_the_stored_file()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var contactId = await CreateContactAsync(db, organizationId);
        var uploaderId = await CreateUserAsync(db);
        var fileStorage = new FakeFileStorage();

        var uploaded = await new UploadAttachmentCommandHandler(db, fileStorage, new FakeCurrentUserService(uploaderId)).Handle(
            new UploadAttachmentCommand(
                organizationId, AttachmentParentType.Contact, contactId, "a.pdf", 4, "application/pdf",
                new MemoryStream(Encoding.UTF8.GetBytes("data"))),
            CancellationToken.None);
        var storageKey = (await db.Attachments.SingleAsync(x => x.Id == uploaded.Id)).StorageKey;

        await new DeleteAttachmentCommandHandler(db, fileStorage).Handle(
            new DeleteAttachmentCommand(organizationId, uploaded.Id), CancellationToken.None);

        Assert.Null(await db.Attachments.SingleOrDefaultAsync(x => x.Id == uploaded.Id));
        Assert.False(fileStorage.Contains(storageKey));
    }

    private static async Task<Guid> CreateContactAsync(IAppDbContext db, Guid organizationId)
    {
        var contact = await new CreateContactCommandHandler(db, new FakeDocumentNumberGenerator()).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Retail", null, null, null, null, null, 0m),
            CancellationToken.None);
        return contact.Id;
    }

    private static async Task<Guid> CreateUserAsync(IAppDbContext db)
    {
        var user = Domain.Identity.User.Register("Uploader", $"{Guid.NewGuid():N}@example.com", "9800000000", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }
}
