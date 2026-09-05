namespace ErpApp.Application.Common.Email;

/// <summary>
/// One outbound message. Phase 30 introduced this because Phase 1a's
/// <c>SendAsync(to, subject, body)</c> cannot express the four things a Send Email dialog produces:
/// more than one To, a CC and a BCC, a Reply-To, and attachments.
///
/// <para>The old three-argument overload survives as a default interface method on
/// <see cref="IEmailSender"/> rather than being swept away — its five callers (verification codes,
/// password reset, user invites, import/export completion notices, scheduled alerts) all genuinely
/// send one plain message to one person, and rewriting them to build a record would be churn
/// dressed as consistency.</para>
///
/// <para><b>Bcc is a real BCC, not a second To.</b> Callers must never fold it into
/// <see cref="To"/>: the whole point of the field is that the recipients do not see each other, and
/// on a customer-facing invoice email that distinction is a privacy leak, not a formatting
/// preference.</para>
/// </summary>
/// <param name="To">At least one address. Validated upstream; a sender may assume non-empty.</param>
/// <param name="IsHtml">Phase 30's document emails are rich text (the live editor is TinyMCE);
/// the older plain-text callers set this false.</param>
public sealed record EmailMessage(
    IReadOnlyList<string> To,
    string Subject,
    string Body,
    IReadOnlyList<string>? Cc = null,
    IReadOnlyList<string>? Bcc = null,
    string? ReplyTo = null,
    IReadOnlyList<EmailAttachment>? Attachments = null,
    bool IsHtml = false)
{
    /// <summary>The Phase 1a shape, for the callers that only ever needed it.</summary>
    public static EmailMessage PlainText(string toEmail, string subject, string body) =>
        new([toEmail], subject, body);
}

/// <summary>
/// One attachment, already in memory. Deliberately bytes rather than a <see cref="Stream"/> or an
/// <c>IFileStorage</c> key: an <see cref="IEmailSender"/> must not need storage or a live request
/// to do its job, and MailKit buffers the content anyway. The caller that reads blobs
/// (<c>EmailSendJobProcessor</c>) is the one place that knows about storage.
/// </summary>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);
