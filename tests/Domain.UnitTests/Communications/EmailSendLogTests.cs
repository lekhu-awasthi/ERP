using ErpApp.Domain.Communications;
using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Communications;

public class EmailSendLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Queues_in_the_Queued_state_so_the_row_exists_before_anything_is_sent()
    {
        var log = Build();

        Assert.Equal(EmailSendStatus.Queued, log.Status);
        Assert.Null(log.CompletedAt);
        Assert.Equal(Now, log.CreatedAt);
    }

    /// <summary>The dialog's own live shape: a Contact send has no "Attach … PDF" checkbox at all,
    /// because there is no document. Rejecting it in the factory means no code path can construct
    /// a row the job would then fail to render.</summary>
    [Fact]
    public void Rejects_a_document_pdf_attachment_on_a_contact_send()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EmailSendLog.Queue(
            Guid.NewGuid(), Guid.NewGuid(), EmailParentType.Contact, Guid.NewGuid(),
            EmailTemplateContext.General, null, "a@example.test", null, null, null,
            "Subject", "Body", attachDocumentPdf: true, Guid.NewGuid(), Now));

        Assert.Contains("no document to attach", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("a@x.test, b@x.test", 2)]
    [InlineData("a@x.test; b@x.test", 2)]
    [InlineData("  a@x.test ,, b@x.test ", 2)]
    [InlineData("a@x.test, A@X.TEST", 1)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void Parses_addresses_on_either_separator_dropping_blanks_and_case_insensitive_duplicates(
        string? raw, int expected)
    {
        Assert.Equal(expected, EmailSendLog.ParseAddresses(raw).Count);
    }

    /// <summary>A duplicate address would otherwise mean one person receives the same invoice
    /// twice from a single send -- the same reason AlertDefinition dedupes its recipients.</summary>
    [Fact]
    public void Normalises_the_stored_lists_so_a_duplicate_recipient_is_stored_once()
    {
        var log = EmailSendLog.Queue(
            Guid.NewGuid(), Guid.NewGuid(), EmailParentType.Invoice, Guid.NewGuid(),
            EmailTemplateContext.Invoice, null, "a@x.test; A@x.test , b@x.test", "  ", null, null,
            "Subject", "Body", attachDocumentPdf: false, Guid.NewGuid(), Now);

        Assert.Equal(["a@x.test", "b@x.test"], log.To);

        // An empty CC is stored as null, so "no CC" has exactly one representation.
        Assert.Null(log.CcAddresses);
        Assert.Empty(log.Cc);
    }

    [Fact]
    public void Marks_sent_and_clears_any_earlier_failure_reason()
    {
        var log = Build();
        log.MarkSending();
        log.MarkFailed(Now, "transient");
        log.MarkSent(Now.AddSeconds(1));

        Assert.Equal(EmailSendStatus.Sent, log.Status);
        Assert.Null(log.FailureReason);
        Assert.Equal(Now.AddSeconds(1), log.CompletedAt);
    }

    /// <summary>An SMTP exception message is arbitrary third-party text; losing its tail must never
    /// fail the save that records the failure. Same contract as AlertSendLog.</summary>
    [Fact]
    public void Truncates_a_failure_reason_rather_than_rejecting_it()
    {
        var log = Build();
        log.MarkFailed(Now, new string('x', 5000));

        Assert.Equal(1000, log.FailureReason!.Length);
        Assert.Equal(EmailSendStatus.Failed, log.Status);
    }

    [Fact]
    public void Purging_attachments_keeps_the_names_and_drops_the_storage_keys()
    {
        var log = Build();
        log.AddAttachment("signed-delivery-note.pdf", "application/pdf", 1234, "key-1");

        log.MarkAttachmentsPurged();

        var attachment = Assert.Single(log.Attachments);
        Assert.Equal("signed-delivery-note.pdf", attachment.FileName);
        Assert.Equal(1234, attachment.SizeBytes);
        Assert.Null(attachment.StorageKey);
        Assert.NotNull(attachment.PurgedAt);
    }

    private static EmailSendLog Build() => EmailSendLog.Queue(
        Guid.NewGuid(), Guid.NewGuid(), EmailParentType.Invoice, Guid.NewGuid(),
        EmailTemplateContext.Invoice, null, "ops@example.test", null, null, null,
        "Invoice 045", "Hello", attachDocumentPdf: true, Guid.NewGuid(), Now);
}
