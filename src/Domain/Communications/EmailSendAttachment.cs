namespace ErpApp.Domain.Communications;

/// <summary>
/// One extra file a user dropped on the Send Email dialog, recorded against its
/// <see cref="EmailSendLog"/>.
///
/// <para><b>Why these are not <c>Attachment</c> rows on the document</b> (Decision E). The dialog's
/// drop zone sits beside an "Attach Invoice PDF" checkbox and reads "Drag and drop or click here to
/// upload files" — it is composing a message, not filing paperwork. Routing the drops into
/// <c>Attachment</c> would make them appear in the document's Documents tab, where a user who
/// attached a signed delivery slip to one email would find it listed as a permanent document of
/// record they never filed. The two are different concepts, and phase 18's Decision #2
/// (<c>Attachment</c> versus <c>WorkTask</c>) is the precedent for checking that before reusing an
/// enum rather than after.</para>
///
/// <para><b>Retention</b> (phase-21b Decision E — a feature that writes a blob decides its deletion
/// story in the same phase). The blob exists for exactly one reason: the background job that sends
/// the message has to read it after the HTTP request that received it has ended. Once the send
/// reaches a terminal status the bytes have done their whole job, so the processor deletes them and
/// then stamps <see cref="PurgedAt"/> — blob first, row second, the same ordering
/// <c>QueuedExportJobProcessor</c> uses, so a failure leaves a harmless orphaned file rather than a
/// row promising bytes that are gone. <see cref="FileName"/> and <see cref="SizeBytes"/> survive,
/// so the log can still say what went out. A resend re-uploads, which costs the user nothing —
/// the dialog is a fresh compose either way.</para>
/// </summary>
public sealed class EmailSendAttachment
{
    public Guid Id { get; private set; }
    public Guid EmailSendLogId { get; private set; }

    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }

    /// <summary>Opaque <c>IFileStorage</c> key. Null once <see cref="PurgedAt"/> is set.</summary>
    public string? StorageKey { get; private set; }

    public DateTimeOffset? PurgedAt { get; private set; }

    private EmailSendAttachment()
    {
    }

    internal static EmailSendAttachment Create(
        Guid emailSendLogId, string fileName, string contentType, long sizeBytes, string storageKey)
    {
        return new EmailSendAttachment
        {
            Id = Guid.NewGuid(),
            EmailSendLogId = emailSendLogId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StorageKey = storageKey,
        };
    }

    internal void MarkPurged()
    {
        StorageKey = null;
        PurgedAt = DateTimeOffset.UtcNow;
    }
}
