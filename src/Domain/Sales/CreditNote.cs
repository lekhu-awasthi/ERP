using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.Sales;

/// <summary>
/// Conversion target of Invoice (architecture-spec.md §3.3/§4.4), same pattern Invoice already
/// established for Quotation -- ReferrerType/ReferrerId point back at the source Invoice.
/// Approve() posts the exact reverse of InvoicePostingRule (Credit Accounts Receivable, Debit
/// each line's Sales Revenue account, Debit VAT Payable) via CreditNotePostingRule, same
/// resolved-input-record split InvoicePostingInput uses.
/// </summary>
public sealed class CreditNote
{
    public const string DraftCode = "DRAFT";

    private readonly List<CreditNoteLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public CreditNoteStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>
    /// Phase 28 (FR-2.5). The currency this document's own amounts are denominated in -- the
    /// three-letter code, not a Currency row's id (see Domain.Tenancy.Currency for why). Defaults
    /// to the base currency, so every document created before this phase, and every document a
    /// single-currency tenant will ever create, needs no special handling anywhere.
    /// </summary>
    public string CurrencyCode { get; private set; } = CurrencyCatalog.BaseCode;

    /// <summary>
    /// This document's rate to the base currency, stored on the document rather than looked up by
    /// date. Confirmed live 2026-09-04: the reference product's "Exchange Rate To NPR*" is a plain
    /// manual number input with no date coupling, and its conversion flow carries the rate along in
    /// the pre-fill snapshot rather than re-deriving it. Exactly 1 for a base-currency document --
    /// an invariant enforced by <see cref="ExchangeRates.Validate"/>, matching the live form, which
    /// disables the input and pins it to 1 whenever the selected currency is NPR.
    /// </summary>
    public decimal ExchangeRate { get; private set; } = ExchangeRates.BaseRate;
    public DocumentType? ReferrerType { get; private set; }
    public Guid? ReferrerId { get; private set; }
    public decimal DiscountPct { get; private set; }

    /// <summary>Phase 27b -- the "+ Add Terms and Conditions" block's stored text (FR-11.3's
    /// CustomTemplate finding its first consumer). Free text on the document, <b>not</b> a pointer
    /// to the CustomTemplate it was seeded from: the reference product pre-fills the editor from a
    /// chosen template and then lets the user edit it freely (confirm-live 2026-09-03), so the
    /// template is a starting point, and a document must keep the words it was actually issued with
    /// even after that template is edited or deleted.</summary>
    public string? Terms { get; private set; }

    public IReadOnlyList<CreditNoteLine> Lines => _lines;

    private CreditNote()
    {
    }

    public static CreditNote Create(
        Guid organizationId, Guid contactId, DateOnly date, string? reference, DocumentType? referrerType, Guid? referrerId,
        decimal discountPct = 0)
    {
        EnsureValidDiscountPct(discountPct);

        return new CreditNote
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            Status = CreditNoteStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            ReferrerType = referrerType,
            ReferrerId = referrerId,
            DiscountPct = discountPct,
        };
    }

    public void UpdateHeader(Guid contactId, DateOnly date, string? reference, decimal discountPct)
    {
        EnsureDraft();
        EnsureValidDiscountPct(discountPct);
        ContactId = contactId;
        Date = date;
        Reference = reference;
        DiscountPct = discountPct;
    }

    public void AddLine(Guid productId, decimal quantity, decimal rate, VatRate vatRate, decimal discountPct)
    {
        EnsureDraft();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("A credit note line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        _lines.Add(CreditNoteLine.Create(Id, productId, quantity, rate, vatRate, discountPct, DiscountPct));
    }

    private static void EnsureValidDiscountPct(decimal discountPct)
    {
        if (discountPct < 0 || discountPct > 100)
        {
            throw new InvalidOperationException("Discount% must be between 0 and 100.");
        }
    }

    public void ClearLines()
    {
        EnsureDraft();
        _lines.Clear();
    }

    public void Approve(Guid approvedByUserId, string code)
    {
        EnsureDraft();

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("A credit note needs at least one line to be approved.");
        }

        Status = CreditNoteStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = CreditNoteStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Draft-only, unlike <c>SetCustomStatus</c>: terms are part of what the document
    /// says, so they follow the same rule as every other header field rather than the
    /// orthogonal-metadata rule Custom Status follows.</summary>
    public void SetTerms(string? terms)
    {
        EnsureDraft();
        Terms = string.IsNullOrWhiteSpace(terms) ? null : terms.Trim();
    }

    /// <summary>
    /// Sets this document's transaction currency and its rate to the base currency. A separate
    /// mutator rather than two more parameters on Create/UpdateHeader, for the same reason
    /// <c>SetExport</c> is one: it is an orthogonal facet of the header with its own invariant
    /// (<see cref="ExchangeRates.Validate"/>), and threading it through every constructor would
    /// change twelve aggregates' signatures to express one fact. Draft-only, like every other
    /// header mutation -- an Approved document's amounts are already posted to the general ledger
    /// at its rate, so changing that rate afterwards would silently invalidate the posting.
    /// </summary>
    public void SetCurrency(string? currencyCode, decimal? exchangeRate)
    {
        EnsureDraft();
        (CurrencyCode, ExchangeRate) = ExchangeRates.Validate(currencyCode, exchangeRate);
    }

    private void EnsureDraft()
    {
        if (Status != CreditNoteStatus.Draft)
        {
            throw new InvalidOperationException("This credit note is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != CreditNoteStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved credit note can be voided.");
        }
    }
}
