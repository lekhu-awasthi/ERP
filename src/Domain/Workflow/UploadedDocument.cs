using ErpApp.Domain.Common;

namespace ErpApp.Domain.Workflow;

/// <summary>
/// One scanned or photographed source document sitting in the tenant's inbox (FR-10.3), waiting to
/// be turned into a real transaction by a human.
///
/// <para><b>The invariant, stated as an invariant, because the next reader will otherwise assume
/// this is an <see cref="Attachment"/> with extra columns.</b> An UploadedDocument is
/// <i>evidence</i>, never a transaction and never a posting. It has no document number, no
/// Draft/Approve/Void lifecycle, no GlJournalEntry, no StockLedgerEntry, no Payment, and no
/// lock-date gate -- nothing it holds is an accounting fact. Its own Pending/Done status says only
/// whether a person has finished dealing with it. Converting it creates a transaction through that
/// transaction's own ordinary Create command, with a human pressing Save; this aggregate merely
/// records, afterwards, which transaction came out (<see cref="LinkedTransactionType"/> /
/// <see cref="LinkedTransactionId"/>) so the posted document can show the scan it was typed from.
/// </para>
///
/// <para><b>Why the link lives here and not on the transaction</b> (docs/phase-22-status.md,
/// Decision A): the transactional aggregates already carry a ReferrerType/ReferrerId pair, and it
/// means something specific -- document-to-document conversion, with the enforcement burden Phase
/// 6's bug #4 catalogued (a Converted status on the source, quantity caps net of prior reversals,
/// Contact/TDS consistency). An inbox scan is none of that: there is nothing to cap, no net effect
/// to trace, and no accounting sense in which it is "converted". Putting the link here costs zero
/// change to any transactional aggregate and matches the reference product's own data model
/// (erp-module-scan.md line 111: linkedTransactionId?, linkedTransactionType?).</para>
///
/// <para><b>One document, one transaction.</b> <see cref="LinkTransaction"/> refuses a second link
/// rather than overwriting or accumulating. A single page really can be the source of a Purchase
/// Bill <i>and</i> the Supplier Payment settling it, but the honest answer to that is two uploads
/// of the same page, not a one-to-many link that would leave "which transaction does the Done tab
/// mean?" unanswerable -- and there is no reversal path here to make an accidental second
/// conversion undoable. Deleting the document is the only exit from a wrong link, and deletion is
/// blocked once a transaction points at it.</para>
///
/// <para><see cref="StorageKey"/> is IFileStorage's opaque key, never a public URL -- reused from
/// Phase 18 exactly as its own doc comment anticipated. Unlike a job artifact (phase-21b's Decision
/// E), an inbox scan is <b>never swept</b>: once converted it is the evidence behind a posted
/// transaction, which a Nepali tenant may be required to retain for years.</para>
/// </summary>
public sealed class UploadedDocument
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string FileName { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public string ContentType { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;

    /// <summary>Free-text note the uploader can type ("Bhatbhateni bill, Shrawan"). Nullable --
    /// the live screen's own upload flow asks for nothing but the file.</summary>
    public string? Description { get; private set; }

    /// <summary>The per-row chip the live grid renders beside a document ("Bill", "Receipt").
    /// Free text, not a lookup -- nothing in the reference product's own data model
    /// (erp-module-scan.md line 111) suggested a managed label list.</summary>
    public string? Label { get; private set; }

    public Guid UploadedByUserId { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
    public UploadedDocumentStatus Status { get; private set; }

    /// <summary>The transaction a human produced from this scan, or null. A
    /// <see cref="DocumentType"/> rather than a bespoke enum: the four supported targets
    /// (Invoice, PurchaseBill, Expense, Payment) are already members of it, and the pair is only
    /// ever read back as "open this document".</summary>
    public DocumentType? LinkedTransactionType { get; private set; }
    public Guid? LinkedTransactionId { get; private set; }
    public DateTimeOffset? LinkedAt { get; private set; }

    public DocumentExtractionStatus ExtractionStatus { get; private set; }

    /// <summary>The extractor's suggestion, serialized. Stored as JSON rather than shredded into
    /// columns because it is <i>not data</i> -- it is a machine's guess, read once by a prefill
    /// query and then thrown away when the human saves. Shredding it into typed columns would
    /// invite exactly the future query that treats it as fact.</summary>
    public string? ExtractedDataJson { get; private set; }

    /// <summary>Which model produced <see cref="ExtractedDataJson"/>, recorded so a later reader
    /// can tell what guessed at a number a human then approved.</summary>
    public string? ExtractionModelId { get; private set; }

    public string? ExtractionFailureReason { get; private set; }
    public DateTimeOffset? ExtractionAttemptedAt { get; private set; }

    private UploadedDocument()
    {
    }

    public static UploadedDocument Create(
        Guid organizationId,
        string fileName,
        long sizeBytes,
        string contentType,
        string storageKey,
        string? description,
        string? label,
        Guid uploadedByUserId,
        DateTimeOffset uploadedAt)
    {
        return new UploadedDocument
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            FileName = fileName,
            SizeBytes = sizeBytes,
            ContentType = contentType,
            StorageKey = storageKey,
            Description = Normalize(description),
            Label = Normalize(label),
            UploadedByUserId = uploadedByUserId,
            UploadedAt = uploadedAt,
            Status = UploadedDocumentStatus.Pending,
            ExtractionStatus = DocumentExtractionStatus.NotAttempted,
        };
    }

    /// <summary>True once a human has produced a transaction from this scan. The single condition
    /// the UI gates its "+ Add as" menu, its Delete button and its Reopen action on -- never
    /// <see cref="Status"/>, which a user can also set by hand.</summary>
    public bool IsLinked => LinkedTransactionId is not null;

    /// <summary>
    /// Records the transaction a human just created from this scan, and files the document. Refuses
    /// a second link -- see the one-document-one-transaction paragraph in this type's own doc
    /// comment. The caller has already verified the target exists in this organization; this method
    /// owns only the aggregate's own rule.
    /// </summary>
    public void LinkTransaction(DocumentType transactionType, Guid transactionId, DateTimeOffset linkedAt)
    {
        if (IsLinked)
        {
            throw new InvalidOperationException(
                "This document has already been converted into a transaction. Upload the file again if you need a second one.");
        }

        LinkedTransactionType = transactionType;
        LinkedTransactionId = transactionId;
        LinkedAt = linkedAt;
        Status = UploadedDocumentStatus.Done;
    }

    /// <summary>Files a document by hand, without converting it -- a receipt the tenant keeps but
    /// never posts. Idempotent.</summary>
    public void MarkDone() => Status = UploadedDocumentStatus.Done;

    /// <summary>Puts a hand-filed document back in the Pending working set. Refuses once a
    /// transaction points at it: Done there is a statement of fact about the ledger, not a
    /// housekeeping flag, and un-setting it would leave a linked document sitting in the inbox
    /// inviting a second conversion that <see cref="LinkTransaction"/> would then refuse.</summary>
    public void Reopen()
    {
        if (IsLinked)
        {
            throw new InvalidOperationException(
                "This document was converted into a transaction and cannot be moved back to Pending.");
        }

        Status = UploadedDocumentStatus.Pending;
    }

    /// <summary>
    /// Records one extraction attempt's outcome. Overwrites any previous attempt -- a re-run is a
    /// fresh suggestion, and keeping a history of machine guesses would be storing the guesses as
    /// data, which this aggregate deliberately does not do.
    /// </summary>
    public void RecordExtraction(
        DocumentExtractionStatus status,
        string? extractedDataJson,
        string? modelId,
        string? failureReason,
        DateTimeOffset attemptedAt)
    {
        if (status == DocumentExtractionStatus.NotAttempted)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status), status, "NotAttempted is the initial state, not an attempt outcome.");
        }

        ExtractionStatus = status;
        ExtractedDataJson = status == DocumentExtractionStatus.Succeeded ? extractedDataJson : null;
        ExtractionModelId = modelId;
        ExtractionFailureReason = status == DocumentExtractionStatus.Succeeded ? null : failureReason;
        ExtractionAttemptedAt = attemptedAt;
    }

    /// <summary>Clears a stored suggestion without touching the file -- the "these numbers are not
    /// mine" escape hatch the honesty requirement needs (docs/phase-22-status.md, Decision C).
    /// Resets <see cref="ExtractionStatus"/> to <see cref="DocumentExtractionStatus.NotAttempted"/>,
    /// so the document reads exactly as it did before extraction ever ran.</summary>
    public void ClearExtraction()
    {
        ExtractionStatus = DocumentExtractionStatus.NotAttempted;
        ExtractedDataJson = null;
        ExtractionModelId = null;
        ExtractionFailureReason = null;
        ExtractionAttemptedAt = null;
    }

    public void UpdateMetadata(string? description, string? label)
    {
        Description = Normalize(description);
        Label = Normalize(label);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
