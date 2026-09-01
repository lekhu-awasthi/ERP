namespace ErpApp.Application.Common.DocumentExtraction;

/// <summary>
/// One extractor's suggestion about what a scanned document says, in a deliberately
/// <b>target-agnostic</b> shape: the same record serves all four conversion targets (Invoice,
/// Purchase Bill, Expense, Quick Payment), which overlap almost entirely at this level (a party, a
/// date, a reference, some lines, a total). Adding a fifth target therefore costs nothing here --
/// see docs/phase-22-status.md, Decision D.
///
/// <para><b>Every field is nullable, and that is the point.</b> An extractor that is unsure must
/// return null rather than a plausible guess: a null renders as an empty box a human fills in,
/// whereas a wrong-but-confident value renders as a pre-filled box a human may not re-read. This
/// record is a suggestion under review, never data -- nothing built from it reaches the General
/// Ledger without a person pressing Save on the target document's own form.</para>
///
/// <para>Persisted as JSON on <c>UploadedDocument.ExtractedDataJson</c>, so the property names here
/// are the wire format. Renaming one is a data change, not a refactor.</para>
/// </summary>
public sealed record ExtractedDocumentData
{
    /// <summary>The counterparty exactly as printed on the document -- the supplier on a bill, the
    /// customer on a sales invoice. Never resolved to a ContactId by the extractor; that lookup is
    /// the prefill query's job, and it only ever matches exactly.</summary>
    public string? PartyName { get; init; }

    /// <summary>The counterparty's PAN/VAT number as printed. Matched exactly against existing
    /// Contacts by the prefill query; an unmatched PAN is shown to the user, never used to create
    /// a Contact.</summary>
    public string? PartyPan { get; init; }

    /// <summary>The document's own date, if legible. Ambiguous day-first/month-first strings are
    /// the extractor's problem to resolve before it gets here (phase-21c's import-date gotcha, same
    /// failure mode) -- if it cannot, it returns null.</summary>
    public DateOnly? DocumentDate { get; init; }

    /// <summary>The supplier's or customer's own document number ("Invoice No. 4471"). Feeds
    /// Reference / SupplierInvoiceReference depending on the target.</summary>
    public string? Reference { get; init; }

    /// <summary>The grand total as printed, used both as the Quick Payment amount and as a
    /// cross-check the conversion screen shows beside the line totals it computed.</summary>
    public decimal? TotalAmount { get; init; }

    /// <summary>The VAT/tax amount as printed, shown as a cross-check only. Never posted --
    /// the target document's own VAT is computed from its lines by the existing engine.</summary>
    public decimal? VatAmount { get; init; }

    public IReadOnlyList<ExtractedDocumentLine> Lines { get; init; } = [];
}

/// <summary>One line item as printed. <see cref="Description"/> is what the document says;
/// resolving it to a ProductId (exact code or name match only) is the prefill query's job.</summary>
public sealed record ExtractedDocumentLine
{
    public string? Description { get; init; }

    public decimal? Quantity { get; init; }

    public decimal? Rate { get; init; }

    public decimal? Amount { get; init; }
}
