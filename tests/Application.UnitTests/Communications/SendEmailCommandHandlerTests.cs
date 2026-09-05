using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Communications.Commands.SendEmail;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Communications;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace ErpApp.Application.UnitTests.Communications;

/// <summary>
/// Covers the queue half of Phase 30's send: the claim, the second permission layer, and
/// do-exactly-once.
///
/// <para><b>What the InMemory provider cannot prove here.</b> It does not enforce unique indexes,
/// so the genuine two-writers race — both inserts in flight, the loser rejected by
/// <c>IX_EmailSendLogs_OrganizationId_RequestId</c> — cannot be exercised. What <i>is</i> covered is
/// the first-line defence (the pre-read that answers a sequential double-submit), which is the case
/// that actually happens: a double-clicked button, or a client retrying a request whose response it
/// never saw. The unique index is the second line, is asserted in the migration, and is verified
/// against real SQL Server during manual E2E — the same split, and the same wording, phase 20e used
/// for the alert scheduler.</para>
/// </summary>
public class SendEmailCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Queues_a_row_before_anything_is_sent()
    {
        var (db, seed, handler) = await BuildAsync();

        var result = await handler.Handle(Command(seed), CancellationToken.None);

        Assert.False(result.AlreadyQueued);

        var log = await db.EmailSendLogs.SingleAsync();
        Assert.Equal(result.EmailSendLogId, log.Id);
        Assert.Equal(EmailSendStatus.Queued, log.Status);
        Assert.Equal(EmailParentType.Invoice, log.ParentType);
        Assert.Equal(EmailTemplateContext.Invoice, log.Context);
        Assert.Equal(seed.UserId, log.SentByUserId);
        Assert.Null(log.CompletedAt);
    }

    /// <summary>
    /// Do-exactly-once. The same RequestId submitted twice yields one row and one future email —
    /// the case a double-clicked Send button produces.
    /// </summary>
    [Fact]
    public async Task The_same_request_id_submitted_twice_yields_exactly_one_row()
    {
        var (db, seed, handler) = await BuildAsync();
        var command = Command(seed);

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        Assert.False(first.AlreadyQueued);
        Assert.True(second.AlreadyQueued);
        Assert.Equal(first.EmailSendLogId, second.EmailSendLogId);

        Assert.Equal(1, await db.EmailSendLogs.CountAsync());
    }

    /// <summary>
    /// The other half of the same decision: a <i>deliberate</i> resend carries a fresh RequestId
    /// (the client mints one per opened dialog) and is a new row, never a retry of the old one.
    /// </summary>
    [Fact]
    public async Task A_resend_with_a_fresh_request_id_is_a_new_row()
    {
        var (db, seed, handler) = await BuildAsync();

        await handler.Handle(Command(seed) with { RequestId = Guid.NewGuid() }, CancellationToken.None);
        await handler.Handle(Command(seed) with { RequestId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(2, await db.EmailSendLogs.CountAsync());
    }

    /// <summary>A duplicate submit re-uploaded its files before reaching the handler; those blobs
    /// now belong to nothing, so they are deleted rather than leaked. Every double-click would
    /// otherwise strand an attachment's worth of storage no row references.</summary>
    [Fact]
    public async Task A_duplicate_submit_deletes_the_blobs_it_re_uploaded()
    {
        var (db, seed, handler, storage) = await BuildWithStorageAsync();
        var command = Command(seed);

        await handler.Handle(command, CancellationToken.None);

        var orphanKey = await SaveBlobAsync(storage, "second-upload");
        await handler.Handle(
            command with
            {
                Attachments = [new SendEmailAttachmentInput("note.pdf", "application/pdf", 4, orphanKey)],
            },
            CancellationToken.None);

        Assert.False(storage.Contains(orphanKey));
        Assert.Equal(1, await db.EmailSendLogs.CountAsync());
    }

    [Fact]
    public async Task Records_dropped_attachments_against_the_send()
    {
        var (db, seed, handler, storage) = await BuildWithStorageAsync();
        var key = await SaveBlobAsync(storage, "hello");

        await handler.Handle(
            Command(seed) with
            {
                Attachments = [new SendEmailAttachmentInput("terms.pdf", "application/pdf", 5, key)],
            },
            CancellationToken.None);

        var attachment = Assert.Single(await db.EmailSendAttachments.ToListAsync());
        Assert.Equal("terms.pdf", attachment.FileName);
        Assert.Equal(key, attachment.StorageKey);
    }

    /// <summary>
    /// The second permission layer. <c>Communication.Email.Send</c> alone is not enough — the caller
    /// must also hold the parent document's own View key, re-checked in the handler once the parent
    /// is known. See PermissionKeys.EmailSend for the derivation.
    /// </summary>
    [Fact]
    public async Task Refuses_a_caller_who_may_send_but_may_not_view_the_document()
    {
        var (db, seed, handler) = await BuildAsync(grantInvoiceView: false);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => handler.Handle(Command(seed), CancellationToken.None));

        Assert.Empty(await db.EmailSendLogs.ToListAsync());
    }

    /// <summary>A document type with no Send Email action is refused outright, rather than
    /// producing a row the job could never render a context for.</summary>
    [Fact]
    public async Task Refuses_a_document_type_that_has_no_send_email_action()
    {
        var (db, seed, handler) = await BuildAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => handler.Handle(
                Command(seed) with { DocumentType = DocumentType.PurchaseBill }, CancellationToken.None));

        Assert.Empty(await db.EmailSendLogs.ToListAsync());
    }

    /// <summary>An id from another organization stays a 404 rather than becoming a probe that
    /// distinguishes "exists elsewhere" from "does not exist".</summary>
    [Fact]
    public async Task An_unknown_parent_is_not_found()
    {
        var (_, seed, handler) = await BuildAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(Command(seed) with { ParentId = Guid.NewGuid() }, CancellationToken.None));
    }

    private static SendEmailCommand Command(Seed seed) => new(
        seed.OrganizationId,
        seed.RequestId,
        DocumentType.Invoice,
        seed.InvoiceId,
        TemplateId: null,
        To: ["customer@example.test"],
        Cc: [],
        Bcc: [],
        ReplyTo: "sales@example.test",
        Subject: "Invoice INV-1",
        Body: "Hello",
        AttachDocumentPdf: true,
        Attachments: []);

    private static async Task<string> SaveBlobAsync(FakeFileStorage storage, string content)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        return await storage.SaveAsync(stream, "note.pdf");
    }

    private sealed record Seed(Guid OrganizationId, Guid UserId, Guid InvoiceId, Guid RequestId);

    private static async Task<(IAppDbContext Db, Seed Seed, SendEmailCommandHandler Handler)> BuildAsync(
        bool grantInvoiceView = true)
    {
        var (db, seed, handler, _) = await BuildWithStorageAsync(grantInvoiceView);
        return (db, seed, handler);
    }

    private static async Task<(IAppDbContext Db, Seed Seed, SendEmailCommandHandler Handler, FakeFileStorage Storage)>
        BuildWithStorageAsync(bool grantInvoiceView = true)
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db, grantInvoiceView);
        var storage = new FakeFileStorage();

        var handler = new SendEmailCommandHandler(
            db, storage, new FakeCurrentUserService(seed.UserId), new FakeTimeProvider(Now));

        return (db, seed, handler, storage);
    }

    private static async Task<Seed> SeedAsync(IAppDbContext db, bool grantInvoiceView)
    {
        var user = User.Register("Sales Person", "sales@example.test", "0000000000", "hash");
        db.Users.Add(user);

        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 4, 1), false, "acme",
            "info@acme.test", "015550000", "PAN123", "https://acme.test", user.Id);
        db.Organizations.Add(organization);

        var keys = grantInvoiceView
            ? new[] { PermissionKeys.EmailSend, PermissionKeys.InvoiceView }
            : [PermissionKeys.EmailSend];

        await PermissionGrantSeed.GrantAsync(db, organization.Id, user.Id, keys);

        var contact = Contact.Create(
            organization.Id, ContactType.Customer, "Adhitya Bhandari", "C0001", null, null, null,
            "adhitya@example.test", null, 0m);
        db.Contacts.Add(contact);

        var warehouse = Warehouse.Create(organization.Id, "Main");
        db.Warehouses.Add(warehouse);

        var invoice = Invoice.Create(
            organization.Id, contact.Id, warehouse.Id, new DateOnly(2026, 9, 2), null, null, null);
        db.Invoices.Add(invoice);

        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(organization.Id, user.Id, invoice.Id, Guid.NewGuid());
    }
}
