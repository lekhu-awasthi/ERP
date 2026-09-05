using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.Communications;

/// <summary>
/// One user-initiated outbound email: what was sent, to whom, about what, and whether it left.
/// Backs the reference product's Email Logs tab on a Contact and its Emails tab on a document —
/// one polymorphic <c>(source, source_id)</c> log serving both, exactly as live
/// (docs/phase-30-status.md, Step 1.5).
///
/// <para><b>One row per send, not per recipient.</b> That is the deliberate difference from
/// <c>AlertSendLog</c>, and it follows from what the key is for. An alert's key is
/// (definition, occurrence date, recipient) because the question it answers is "has today's slot
/// already gone to this person?", so the recipient has to be in the key. A user pressing Send
/// composes <i>one message</i> with a To, a CC and a BCC; splitting it into three rows would
/// misreport one action as three and would make the CC list unreconstructable. So the addresses
/// are stored as they were typed and the whole message is one ledger entry.</para>
///
/// <para><b>Idempotency comes from <see cref="RequestId"/>, under a unique index on
/// (OrganizationId, RequestId).</b> An occurrence key like the alert scheduler's is unavailable
/// and would be wrong if it existed: a user who deliberately emails the same invoice to the same
/// person twice means it, and the roadmap fixes that semantic — <i>a resend is a new row, never a
/// retry</i>. What must not happen is one intent becoming two emails because a double-click or a
/// client retry sent the command twice. So the client mints a RequestId when it opens the dialog;
/// the second insert loses the unique index and the handler returns the first row. Reopening the
/// dialog mints a new one, and that resend is a new row, as intended.</para>
///
/// <para><b>Delivery is at-most-once</b>, the same choice and the same reason as phase 20e: the
/// <see cref="EmailSendStatus.Queued"/> row is committed before anything is handed to SMTP, and a
/// row found in <see cref="EmailSendStatus.Sending"/> is never re-claimed. A process that dies
/// mid-send leaves a visible stuck row rather than a customer receiving the same invoice twice, and
/// the argument is stronger here than for a daily summary — this message is addressed to somebody
/// outside the tenant, by name, about their money.</para>
///
/// <para><b>Content is stored resolved</b>, not as a template reference: <see cref="Subject"/> and
/// <see cref="Body"/> are the text actually sent, merge fields already substituted and any user
/// edits already applied. <see cref="TemplateId"/> is attribution only and may dangle if the
/// template is later deleted or rewritten — the same reasoning that makes <c>SmsLog.Content</c>
/// resolved text, and the same reasoning behind phase 27b storing a document's <i>own</i> terms
/// rather than a pointer to the template they came from.</para>
/// </summary>
public sealed class EmailSendLog
{
    /// <summary>Separator for the stored address lists. Matches
    /// <see cref="AlertDefinition.RecipientSeparator"/>, and <see cref="ParseAddresses"/> accepts a
    /// semicolon too for the same reason that one does.</summary>
    public const char AddressSeparator = ',';

    private const int MaxFailureReasonLength = 1000;

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }

    /// <summary>Client-minted, one per opened dialog. See the type-level remarks.</summary>
    public Guid RequestId { get; private set; }

    public EmailParentType ParentType { get; private set; }
    public Guid ParentId { get; private set; }

    /// <summary>Denormalised so the log survives the template being edited or deleted and still
    /// reads correctly — the same reason <c>AlertSendLog</c> copies its <c>AlertType</c>.</summary>
    public EmailTemplateContext Context { get; private set; }

    /// <summary>Attribution only; may point at a deleted template. Null when the tenant had no
    /// template for this context and the built-in default body was used.</summary>
    public Guid? TemplateId { get; private set; }

    public string ToAddresses { get; private set; } = null!;
    public string? CcAddresses { get; private set; }
    public string? BccAddresses { get; private set; }
    public string? ReplyTo { get; private set; }

    public string Subject { get; private set; } = null!;
    public string Body { get; private set; } = null!;

    /// <summary>Whether the parent document's PDF was attached. Always false for a
    /// <see cref="EmailParentType.Contact"/> send, which has no document — live, that dialog has no
    /// such checkbox at all.</summary>
    public bool AttachDocumentPdf { get; private set; }

    public EmailSendStatus Status { get; private set; }
    public string? FailureReason { get; private set; }

    public Guid SentByUserId { get; private set; }

    /// <summary>
    /// Concurrency token, and the runner's claim mechanism: two runners that read the same Queued
    /// row both call <see cref="MarkSending"/>, and the second <c>SaveChangesAsync</c> throws
    /// <c>DbUpdateConcurrencyException</c> rather than silently sending a second copy.
    ///
    /// <para><b>Phase-21a's bug 1 does not apply here, and the reason is worth stating.</b> That
    /// bug — a cancel wedging a running import — happened because <c>ImportJob</c> has <i>two</i>
    /// legitimate writers, the runner and the user's cancel command, so a rowversion bumped by
    /// either invalidated the other's next write. This row has exactly one writer after creation:
    /// nothing edits a send, and a resend is a new row by design. So the token is free of that
    /// conflict and buys real compare-and-set, which <c>ImportJob</c> and <c>ExportJob</c> had to do
    /// without.</para>
    /// </summary>
    public byte[] RowVersion { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private readonly List<EmailSendAttachment> _attachments = [];

    /// <summary>Extra files the user dropped on the dialog. Encapsulated collection — see
    /// AppDbContext's configuration and <c>TestAppDbContext</c>, which must restate it (phase 4's
    /// gotcha).</summary>
    public IReadOnlyCollection<EmailSendAttachment> Attachments => _attachments;

    private EmailSendLog()
    {
    }

    /// <summary>Claims the send. Commit this before handing anything to SMTP.</summary>
    public static EmailSendLog Queue(
        Guid organizationId,
        Guid requestId,
        EmailParentType parentType,
        Guid parentId,
        EmailTemplateContext context,
        Guid? templateId,
        string toAddresses,
        string? ccAddresses,
        string? bccAddresses,
        string? replyTo,
        string subject,
        string body,
        bool attachDocumentPdf,
        Guid sentByUserId,
        DateTimeOffset now)
    {
        if (parentType == EmailParentType.Contact && attachDocumentPdf)
        {
            throw new InvalidOperationException(
                "A Contact has no document to attach; AttachDocumentPdf is only meaningful for a document send.");
        }

        return new EmailSendLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            RequestId = requestId,
            ParentType = parentType,
            ParentId = parentId,
            Context = context,
            TemplateId = templateId,
            ToAddresses = Normalize(toAddresses)!,
            CcAddresses = Normalize(ccAddresses),
            BccAddresses = Normalize(bccAddresses),
            ReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo.Trim(),
            Subject = subject,
            Body = body,
            AttachDocumentPdf = attachDocumentPdf,
            Status = EmailSendStatus.Queued,
            SentByUserId = sentByUserId,
            CreatedAt = now,
        };
    }

    /// <summary>Records one dropped file. Returns the child so the caller can <c>Add</c> it through
    /// the child DbSet — appending to a tracked parent's encapsulated collection is detected as
    /// Modified rather than Added (phase-24 bug #1), so the Domain reports the change and the
    /// handler persists it.</summary>
    public EmailSendAttachment AddAttachment(string fileName, string contentType, long sizeBytes, string storageKey)
    {
        var attachment = EmailSendAttachment.Create(Id, fileName, contentType, sizeBytes, storageKey);
        _attachments.Add(attachment);
        return attachment;
    }

    /// <summary>Takes a Queued row. Only ever called on a Queued row — see
    /// <see cref="EmailSendStatus.Sending"/> for why a Sending row is never re-claimed.</summary>
    public void MarkSending() => Status = EmailSendStatus.Sending;

    public void MarkSent(DateTimeOffset now)
    {
        Status = EmailSendStatus.Sent;
        FailureReason = null;
        CompletedAt = now;
    }

    /// <summary>Reason is truncated rather than rejected: an SMTP exception message is arbitrary
    /// third-party text, and losing its tail must never fail the SaveChanges that records the
    /// failure. Same contract as <c>AlertSendLog.MarkFailed</c>.</summary>
    public void MarkFailed(DateTimeOffset now, string reason)
    {
        Status = EmailSendStatus.Failed;
        FailureReason = reason.Length > MaxFailureReasonLength ? reason[..MaxFailureReasonLength] : reason;
        CompletedAt = now;
    }

    /// <summary>Clears every attachment's storage key once the blobs are gone, so the log keeps
    /// saying <i>what</i> was attached without pointing at bytes that no longer exist. See
    /// <c>EmailSendAttachment</c> for the retention story.</summary>
    public void MarkAttachmentsPurged()
    {
        foreach (var attachment in _attachments)
        {
            attachment.MarkPurged();
        }
    }

    public IReadOnlyList<string> To => ParseAddresses(ToAddresses);

    public IReadOnlyList<string> Cc => ParseAddresses(CcAddresses);

    public IReadOnlyList<string> Bcc => ParseAddresses(BccAddresses);

    /// <summary>Splits on either separator, trims, drops blanks, removes case-insensitive
    /// duplicates. Identical contract to <see cref="AlertDefinition.ParseRecipients"/> — a
    /// duplicate address means one person gets the same invoice twice.</summary>
    public static IReadOnlyList<string> ParseAddresses(string? addresses)
    {
        if (string.IsNullOrWhiteSpace(addresses))
        {
            return [];
        }

        return addresses
            .Split([AddressSeparator, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? Normalize(string? addresses)
    {
        var parsed = ParseAddresses(addresses);
        return parsed.Count == 0 ? null : string.Join(AddressSeparator, parsed);
    }
}
