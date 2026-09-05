using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Communications;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Communications;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ErpApp.Application.UnitTests.Communications;

/// <summary>
/// The send half. See <see cref="EmailSendJobProcessor"/> for the claim-then-act contract.
///
/// <para><b>What the InMemory provider cannot prove:</b> it does not enforce concurrency tokens, so
/// the genuine two-runners race cannot be exercised here. What is covered is everything the token
/// exists to protect around — a Sending row is never picked up again, terminal statuses are written
/// once, and blobs are released. The token itself is asserted in the migration and verified against
/// real SQL Server during manual E2E, the same split phase 20e used.</para>
/// </summary>
public class EmailSendJobProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sends_a_queued_email_and_marks_it_sent()
    {
        var (db, processor, sender, _) = Build();
        var log = Queue(db);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));

        var message = Assert.Single(sender.SentMessages);
        Assert.Equal(["customer@example.test"], message.To);
        Assert.Equal("Invoice 045", message.Subject);

        // The live composer is rich text, so a document email goes out as HTML.
        Assert.True(message.IsHtml);

        var reloaded = await db.EmailSendLogs.SingleAsync(x => x.Id == log.Id);
        Assert.Equal(EmailSendStatus.Sent, reloaded.Status);
        Assert.Equal(Now, reloaded.CompletedAt);
        Assert.Null(reloaded.FailureReason);
    }

    /// <summary>CC and BCC survive as themselves. Folding BCC into To would be a privacy leak on a
    /// customer-facing invoice, not a formatting choice — see EmailMessage's remarks.</summary>
    [Fact]
    public async Task Carries_cc_bcc_and_reply_to_through_to_the_message()
    {
        var (db, processor, sender, _) = Build();
        Queue(db, cc: "boss@example.test", bcc: "audit@example.test", replyTo: "sales@example.test");
        await db.SaveChangesAsync(CancellationToken.None);

        await processor.ProcessNextAsync(CancellationToken.None);

        var message = Assert.Single(sender.SentMessages);
        Assert.Equal(["boss@example.test"], message.Cc);
        Assert.Equal(["audit@example.test"], message.Bcc);
        Assert.Equal("sales@example.test", message.ReplyTo);
        Assert.DoesNotContain("audit@example.test", message.To);
    }

    [Fact]
    public async Task Returns_false_when_there_is_nothing_queued()
    {
        var (_, processor, sender, _) = Build();

        Assert.False(await processor.ProcessNextAsync(CancellationToken.None));
        Assert.Empty(sender.SentMessages);
    }

    /// <summary>
    /// At-most-once, the phase's central delivery choice. A row already in Sending — the shape a
    /// process that died mid-send leaves behind — is never picked up again. Sending a customer their
    /// invoice twice is worse than a visibly stalled row.
    /// </summary>
    [Fact]
    public async Task Never_re_claims_a_row_left_in_Sending()
    {
        var (db, processor, sender, _) = Build();
        var log = Queue(db);
        log.MarkSending();
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.False(await processor.ProcessNextAsync(CancellationToken.None));
        Assert.Empty(sender.SentMessages);

        var reloaded = await db.EmailSendLogs.SingleAsync(x => x.Id == log.Id);
        Assert.Equal(EmailSendStatus.Sending, reloaded.Status);
    }

    [Fact]
    public async Task A_sent_row_is_not_sent_again_on_the_next_tick()
    {
        var (db, processor, sender, _) = Build();
        Queue(db);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        Assert.False(await processor.ProcessNextAsync(CancellationToken.None));

        Assert.Single(sender.SentMessages);
    }

    /// <summary>An SMTP failure is an outcome, recorded against the row, not an exception that kills
    /// the runner's tick — the same contract AlertDispatcher has.</summary>
    [Fact]
    public async Task Records_a_failed_send_rather_than_throwing()
    {
        var db = TestAppDbContext.Create();
        var sender = new ThrowingEmailSender();
        var processor = BuildWith(db, sender, new FakeFileStorage(), new StubPdfRenderer());

        var log = Queue(db);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));

        var reloaded = await db.EmailSendLogs.SingleAsync(x => x.Id == log.Id);
        Assert.Equal(EmailSendStatus.Failed, reloaded.Status);
        Assert.Contains("SMTP refused", reloaded.FailureReason!, StringComparison.Ordinal);
        Assert.Equal(Now, reloaded.CompletedAt);
    }

    [Fact]
    public async Task Attaches_the_document_pdf_from_the_print_pipeline()
    {
        var (db, processor, sender, _) = Build();
        Queue(db, attachPdf: true);
        await db.SaveChangesAsync(CancellationToken.None);

        await processor.ProcessNextAsync(CancellationToken.None);

        var attachment = Assert.Single(Assert.Single(sender.SentMessages).Attachments!);
        Assert.Equal("Invoice_INV-1.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
    }

    /// <summary>
    /// The blobs exist only so this job can read them after the request that received them ended.
    /// Once the send is terminal they are deleted and the keys cleared — but the file <i>names</i>
    /// survive, so the Email Logs tab can still say what went out.
    /// </summary>
    [Fact]
    public async Task Releases_attachment_blobs_once_the_send_is_terminal_but_keeps_their_names()
    {
        var db = TestAppDbContext.Create();
        var storage = new FakeFileStorage();
        var sender = new FakeEmailSender();
        var processor = BuildWith(db, sender, storage, new StubPdfRenderer());

        using var content = new MemoryStream("hello"u8.ToArray());
        var key = await storage.SaveAsync(content, "terms.pdf");

        var log = Queue(db, attachPdf: false);
        db.EmailSendAttachments.Add(log.AddAttachment("terms.pdf", "application/pdf", 5, key));
        await db.SaveChangesAsync(CancellationToken.None);

        await processor.ProcessNextAsync(CancellationToken.None);

        // It reached the message before it was released.
        var attachment = Assert.Single(Assert.Single(sender.SentMessages).Attachments!);
        Assert.Equal("terms.pdf", attachment.FileName);
        Assert.Equal("hello"u8.ToArray(), attachment.Content);

        Assert.False(storage.Contains(key));

        var stored = await db.EmailSendAttachments.SingleAsync();
        Assert.Equal("terms.pdf", stored.FileName);
        Assert.Null(stored.StorageKey);
        Assert.NotNull(stored.PurgedAt);
    }

    /// <summary>
    /// Oldest first, so a queue that briefly backs up still sends in the order users pressed Send.
    ///
    /// <para>Note the <b>fresh processor per job</b>. That is not test tidiness — it mirrors
    /// <c>QueuedJobRunnerHostedService</c>, which creates a DI scope per job precisely because
    /// <c>IJobActingUser</c> is single-shot per scope. Reusing one processor across two sends throws
    /// "this scope has already assumed a different acting user", which is the mechanism doing its
    /// job: one send can never act as another sender's.</para>
    /// </summary>
    [Fact]
    public async Task Sends_the_oldest_queued_email_first()
    {
        var db = TestAppDbContext.Create();
        var sender = new FakeEmailSender();
        var storage = new FakeFileStorage();

        Queue(db, subject: "Second", createdAt: Now.AddMinutes(1));
        Queue(db, subject: "First", createdAt: Now);
        await db.SaveChangesAsync(CancellationToken.None);

        await BuildWith(db, sender, storage, new StubPdfRenderer()).ProcessNextAsync(CancellationToken.None);
        await BuildWith(db, sender, storage, new StubPdfRenderer()).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(["First", "Second"], sender.SentMessages.Select(x => x.Subject));
    }

    /// <summary>The other half of that contract, asserted directly so the reason the test above
    /// builds two processors is written down as a fact rather than a convention.</summary>
    [Fact]
    public async Task One_scope_cannot_send_two_emails_from_different_senders()
    {
        var (db, processor, _, _) = Build();
        Queue(db, subject: "First");
        Queue(db, subject: "Second", createdAt: Now.AddMinutes(1));
        await db.SaveChangesAsync(CancellationToken.None);

        await processor.ProcessNextAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessNextAsync(CancellationToken.None));
    }

    private static EmailSendLog Queue(
        IAppDbContext db,
        string? cc = null,
        string? bcc = null,
        string? replyTo = null,
        bool attachPdf = false,
        string subject = "Invoice 045",
        DateTimeOffset? createdAt = null)
    {
        var log = EmailSendLog.Queue(
            Guid.NewGuid(), Guid.NewGuid(), EmailParentType.Invoice, Guid.NewGuid(),
            EmailTemplateContext.Invoice, null, "customer@example.test", cc, bcc, replyTo,
            subject, "<p>Hello</p>", attachPdf, Guid.NewGuid(), createdAt ?? Now);

        db.EmailSendLogs.Add(log);
        return log;
    }

    private static (IAppDbContext Db, EmailSendJobProcessor Processor, FakeEmailSender Sender, FakeFileStorage Storage)
        Build()
    {
        var db = TestAppDbContext.Create();
        var sender = new FakeEmailSender();
        var storage = new FakeFileStorage();
        return (db, BuildWith(db, sender, storage, new StubPdfRenderer()), sender, storage);
    }

    private static EmailSendJobProcessor BuildWith(
        IAppDbContext db,
        Application.Common.Email.IEmailSender sender,
        FakeFileStorage storage,
        IDocumentPdfRenderer renderer) =>
        new(db, sender, storage, renderer, new JobActingUser(), new FakeTimeProvider(Now),
            NullLogger<EmailSendJobProcessor>.Instance);

    /// <summary>Stands in for the Api's QuestPDF renderer, which Application cannot reference.</summary>
    private sealed class StubPdfRenderer : IDocumentPdfRenderer
    {
        public Task<RenderedDocumentPdf> RenderAsync(
            Guid organizationId, DocumentType documentType, Guid documentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RenderedDocumentPdf($"{documentType}_INV-1.pdf", [1, 2, 3]));
    }

    private sealed class ThrowingEmailSender : Application.Common.Email.IEmailSender
    {
        public Task SendAsync(
            Application.Common.Email.EmailMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SMTP refused the message.");
    }
}
