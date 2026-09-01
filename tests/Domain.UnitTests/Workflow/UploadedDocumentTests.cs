using ErpApp.Domain.Common;
using ErpApp.Domain.Workflow;

namespace ErpApp.Domain.UnitTests.Workflow;

/// <summary>
/// Phase 22 (FR-10.3). These assert the aggregate's own invariants -- the ones stated in prose at
/// the top of <see cref="UploadedDocument"/>, because the next reader will otherwise assume an inbox
/// document is an <see cref="Attachment"/> with extra columns.
/// </summary>
public class UploadedDocumentTests
{
    private static UploadedDocument NewDocument(string fileName = "bill.pdf") =>
        UploadedDocument.Create(
            Guid.NewGuid(), fileName, 1024, "application/pdf", Guid.NewGuid().ToString("N"),
            "  a note  ", "  Bill  ", Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public void A_new_document_is_pending_unlinked_and_never_extracted()
    {
        var document = NewDocument();

        Assert.Equal(UploadedDocumentStatus.Pending, document.Status);
        Assert.Equal(DocumentExtractionStatus.NotAttempted, document.ExtractionStatus);
        Assert.False(document.IsLinked);
        Assert.Null(document.LinkedTransactionType);
        Assert.Null(document.LinkedTransactionId);
        Assert.Null(document.LinkedAt);
        Assert.Null(document.ExtractedDataJson);
    }

    [Fact]
    public void Create_trims_the_free_text_fields_and_nulls_out_blanks()
    {
        var trimmed = NewDocument();
        Assert.Equal("a note", trimmed.Description);
        Assert.Equal("Bill", trimmed.Label);

        trimmed.UpdateMetadata("   ", string.Empty);
        Assert.Null(trimmed.Description);
        Assert.Null(trimmed.Label);
    }

    [Fact]
    public void Linking_a_transaction_files_the_document_as_done()
    {
        var document = NewDocument();
        var transactionId = Guid.NewGuid();
        var linkedAt = DateTimeOffset.UtcNow;

        document.LinkTransaction(DocumentType.PurchaseBill, transactionId, linkedAt);

        Assert.True(document.IsLinked);
        Assert.Equal(DocumentType.PurchaseBill, document.LinkedTransactionType);
        Assert.Equal(transactionId, document.LinkedTransactionId);
        Assert.Equal(linkedAt, document.LinkedAt);
        Assert.Equal(UploadedDocumentStatus.Done, document.Status);
    }

    /// <summary>One document, one transaction -- the aggregate refuses rather than overwriting or
    /// accumulating, because there is no reversal path to make an accidental second conversion
    /// undoable.</summary>
    [Fact]
    public void A_second_link_is_refused_and_leaves_the_first_intact()
    {
        var document = NewDocument();
        var first = Guid.NewGuid();
        document.LinkTransaction(DocumentType.PurchaseBill, first, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(
            () => document.LinkTransaction(DocumentType.Invoice, Guid.NewGuid(), DateTimeOffset.UtcNow));

        Assert.Equal(first, document.LinkedTransactionId);
        Assert.Equal(DocumentType.PurchaseBill, document.LinkedTransactionType);
    }

    [Fact]
    public void MarkDone_and_Reopen_move_an_unlinked_document_between_the_two_tabs()
    {
        var document = NewDocument();

        document.MarkDone();
        Assert.Equal(UploadedDocumentStatus.Done, document.Status);

        document.MarkDone();
        Assert.Equal(UploadedDocumentStatus.Done, document.Status);

        document.Reopen();
        Assert.Equal(UploadedDocumentStatus.Pending, document.Status);
    }

    /// <summary>Done on a linked document is a statement about the ledger, not a housekeeping
    /// flag.</summary>
    [Fact]
    public void Reopen_is_refused_once_a_transaction_points_at_the_document()
    {
        var document = NewDocument();
        document.LinkTransaction(DocumentType.Expense, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(document.Reopen);
        Assert.Equal(UploadedDocumentStatus.Done, document.Status);
    }

    [Fact]
    public void A_successful_extraction_keeps_the_payload_and_clears_any_previous_failure()
    {
        var document = NewDocument();
        var at = DateTimeOffset.UtcNow;

        document.RecordExtraction(DocumentExtractionStatus.Failed, null, "m1", "vendor was down", at);
        Assert.Equal("vendor was down", document.ExtractionFailureReason);

        document.RecordExtraction(DocumentExtractionStatus.Succeeded, "{\"partyName\":\"X\"}", "m1", null, at);

        Assert.Equal(DocumentExtractionStatus.Succeeded, document.ExtractionStatus);
        Assert.Equal("{\"partyName\":\"X\"}", document.ExtractedDataJson);
        Assert.Null(document.ExtractionFailureReason);
        Assert.Equal(at, document.ExtractionAttemptedAt);
    }

    /// <summary>A failed run must not leave a stale suggestion behind that the conversion form would
    /// then pre-fill from as if it were fresh.</summary>
    [Fact]
    public void A_failed_extraction_discards_any_previously_stored_suggestion()
    {
        var document = NewDocument();
        document.RecordExtraction(DocumentExtractionStatus.Succeeded, "{\"partyName\":\"X\"}", "m1", null, DateTimeOffset.UtcNow);

        document.RecordExtraction(DocumentExtractionStatus.Failed, null, "m1", "timed out", DateTimeOffset.UtcNow);

        Assert.Null(document.ExtractedDataJson);
        Assert.Equal("timed out", document.ExtractionFailureReason);
    }

    [Fact]
    public void NotAttempted_is_not_an_attempt_outcome()
    {
        var document = NewDocument();

        Assert.Throws<ArgumentOutOfRangeException>(() => document.RecordExtraction(
            DocumentExtractionStatus.NotAttempted, null, null, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Clearing_an_extraction_returns_the_document_to_its_pre_extraction_state()
    {
        var document = NewDocument();
        document.RecordExtraction(DocumentExtractionStatus.Succeeded, "{}", "m1", null, DateTimeOffset.UtcNow);

        document.ClearExtraction();

        Assert.Equal(DocumentExtractionStatus.NotAttempted, document.ExtractionStatus);
        Assert.Null(document.ExtractedDataJson);
        Assert.Null(document.ExtractionModelId);
        Assert.Null(document.ExtractionFailureReason);
        Assert.Null(document.ExtractionAttemptedAt);
    }
}
