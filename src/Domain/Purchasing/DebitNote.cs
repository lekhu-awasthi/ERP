using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.Purchasing;

/// <summary>
/// Mirror of Sales.CreditNote -- conversion target of PurchaseBill (architecture-spec.md §3.3/
/// §4.5), same pattern PurchaseBill established for PurchaseOrder. Approve() posts the exact
/// reverse of PurchaseBillPostingRule (Debit Accounts Payable net of TDS, Debit TDS Payable,
/// Credit each line's Purchase Account, Credit VAT Receivable) via DebitNotePostingRule.
///
/// Carries its own TdsTypeId/TdsAmount (resolved server-side from this DebitNote's own lines,
/// same PurchasingValidation.ResolveTdsAmountAsync path PurchaseBill/Expense use) -- an earlier
/// version of this aggregate had no TDS fields at all on the theory that "a reversal doesn't
/// reverse the TDS withholding", but that left DebitNotePostingRule debiting Accounts Payable for
/// the *full* grand total while the original PurchaseBill had only credited AP net of TDS: a full
/// reversal then left Accounts Payable off by the TDS amount and TDS Payable never resolved,
/// a real ledger imbalance, not a cosmetic gap. See phase-6-status.md's bug #3.
/// </summary>
public sealed class DebitNote
{
    public const string DraftCode = "DRAFT";

    private readonly List<DebitNoteLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public Guid? TdsTypeId { get; private set; }
    public decimal TdsAmount { get; private set; }
    public DebitNoteStatus Status { get; private set; }
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

    public IReadOnlyList<DebitNoteLine> Lines => _lines;

    private DebitNote()
    {
    }

    public static DebitNote Create(
        Guid organizationId,
        Guid contactId,
        DateOnly date,
        string? reference,
        Guid? tdsTypeId,
        decimal tdsAmount,
        DocumentType? referrerType,
        Guid? referrerId,
        decimal discountPct = 0)
    {
        EnsureValidDiscountPct(discountPct);

        return new DebitNote
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            TdsTypeId = tdsTypeId,
            TdsAmount = tdsAmount,
            Status = DebitNoteStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            ReferrerType = referrerType,
            ReferrerId = referrerId,
            DiscountPct = discountPct,
        };
    }

    public void UpdateHeader(
        Guid contactId, DateOnly date, string? reference, Guid? tdsTypeId, decimal tdsAmount, decimal discountPct)
    {
        EnsureDraft();
        EnsureValidDiscountPct(discountPct);
        ContactId = contactId;
        Date = date;
        Reference = reference;
        TdsTypeId = tdsTypeId;
        TdsAmount = tdsAmount;
        DiscountPct = discountPct;
    }

    public void AddLine(Guid productId, decimal quantity, decimal rate, VatRate vatRate, decimal discountPct)
    {
        EnsureDraft();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("A debit note line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        _lines.Add(DebitNoteLine.Create(Id, productId, quantity, rate, vatRate, discountPct, DiscountPct));
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
            throw new InvalidOperationException("A debit note needs at least one line to be approved.");
        }

        Status = DebitNoteStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = DebitNoteStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
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
        if (Status != DebitNoteStatus.Draft)
        {
            throw new InvalidOperationException("This debit note is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != DebitNoteStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved debit note can be voided.");
        }
    }
}
