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
    /// Phase 32 (FR-2.3/FR-3.3). The billing location this document was raised from -- the
    /// borderless picker in the live document header, left of Save, rendering "Name (Code)" and
    /// defaulting to HeadOffice (confirmed live 2026-09-07 on a location-enabled tenant).
    ///
    /// <para>Nullable, and nullable on every transactional aggregate rather than only the sales-side
    /// ones, because <see cref="Domain.Tenancy.LocationScopeMode"/> is a runtime setting an Admin can
    /// widen at any moment: the column has to exist before the switch is flipped, or the switch is a
    /// lie. <see cref="Domain.Tenancy.DocumentLocationScope"/> is the single place that decides
    /// whether a given type carries one for a given tenant.</para>
    ///
    /// <para>An Id rather than a code, unlike <see cref="CurrencyCode"/> directly above -- see
    /// Domain.Tenancy.BillingLocation for why the two diverge.</para>
    /// </summary>
    public Guid? LocationId { get; private set; }

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

    /// <summary>Phase 63 -- where the credit note was raised: <see cref="SalesChannel.Pos"/> for a refund
    /// at a till, <see cref="SalesChannel.Erp"/> for everything else (every row before this phase).</summary>
    public SalesChannel Channel { get; private set; }

    /// <summary>Phase 63 -- the session whose drawer a till refund was paid out of. That is the session
    /// <b>open when the refund was made</b>, which for yesterday's sale is today's, never the sale's own
    /// (phase-63-status.md Decision C).</summary>
    public Guid? PosSessionId { get; private set; }

    /// <summary>Phase 63 -- a till refund's rounding to the rupee, signed like
    /// <see cref="Invoice.RoundOff"/>: positive gives back more than the lines, negative less. Zero on
    /// every ERP credit note.</summary>
    public decimal RoundOff { get; private set; }

    /// <summary>Phase 63 -- why the goods came back. Required at the till (the vendor's Remarks*), and it
    /// is what VAT Rules Rule 20's "details of ... credit" asks the note to carry.</summary>
    public string? Reason { get; private set; }

    public const int MaxReasonLength = 500;

    private readonly List<CreditNotePayout> _payouts = [];

    public IReadOnlyList<CreditNoteLine> Lines => _lines;

    /// <summary>Phase 63 -- what a till refund handed back, per mode. Empty on an ERP credit note.</summary>
    public IReadOnlyList<CreditNotePayout> Payouts => _payouts;

    /// <summary>What the note takes off what the customer owes in all: every line's amount, service
    /// charge and VAT, plus the round-off. For an ERP note it is Σ(Amount + VAT), as before.</summary>
    public decimal GrandTotal => _lines.Sum(x => x.LineTotal) + RoundOff;

    public decimal ServiceChargeTotal => _lines.Sum(x => x.ServiceChargeAmount);

    /// <summary>Phase 63 -- what was handed back over the counter.</summary>
    public decimal PaidOutAmount => _payouts.Sum(x => x.Amount);

    /// <summary>Phase 63 -- what was left on the customer's account instead: the part of the refund that
    /// settles what they still owed on the sale.</summary>
    public decimal ToAccountAmount => GrandTotal - PaidOutAmount;

    private CreditNote()
    {
    }

    /// <summary>
    /// Phase 63 -- a refund at a till: a credit note against the till sale <paramref name="invoiceId"/>,
    /// raised in <paramref name="posSessionId"/> at <paramref name="locationId"/>, carrying the sale's
    /// bill discount. It is approved by the same command that creates it, like the sale.
    /// </summary>
    public static CreditNote CreatePosRefund(
        Guid organizationId, Guid contactId, DateOnly date, Guid locationId, Guid posSessionId, Guid invoiceId,
        string invoiceCode, decimal discountPct, string reason)
    {
        if (locationId == Guid.Empty || posSessionId == Guid.Empty || invoiceId == Guid.Empty)
        {
            throw new InvalidOperationException("A till refund names its location, its session and the sale it returns.");
        }

        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new InvalidOperationException("A refund says why the goods came back.");
        }

        if (trimmed.Length > MaxReasonLength)
        {
            throw new InvalidOperationException($"A reason is at most {MaxReasonLength} characters.");
        }

        // The reference is the sale's number, as the vendor stores it (reference_no = the invoice code)
        // and as Rule 20 asks: the note names the tax invoice it relates to.
        var note = Create(organizationId, contactId, date, invoiceCode, DocumentType.Invoice, invoiceId, discountPct);
        note.Channel = SalesChannel.Pos;
        note.PosSessionId = posSessionId;
        note.LocationId = locationId;
        note.Reason = trimmed;
        return note;
    }

    /// <summary>Phase 63 -- one returned line, at the sale line's rate, discount, VAT rate, unit factor
    /// and service charge rate. The caller copies those from the sale line; the quantity is what came
    /// back.</summary>
    public void AddPosLine(
        Guid productId, decimal quantity, decimal rate, VatRate vatRate, decimal discountPct,
        Guid? unitId, decimal conversionFactor, Guid? batchId, decimal serviceChargeRate)
    {
        EnsureDraft();
        EnsurePos();
        EnsureUnpaid();

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("A credit note line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        _lines.Add(CreditNoteLine.CreatePos(
            Id, productId, quantity, rate, vatRate, discountPct, DiscountPct, batchId, unitId, conversionFactor,
            serviceChargeRate));

        RoundOff = 0m;
    }

    /// <summary>
    /// Phase 63 -- sets the refund's round-off. The caller decides it (nearest rupee, or exactly what is
    /// left of the sale when this refund returns the last of it -- see
    /// <c>CreatePosRefundCommandHandler</c>), because only the caller can see the sale's earlier
    /// refunds. The aggregate holds the shape: whole paisa, and never a refund of less than nothing.
    /// </summary>
    public void SetRoundOff(decimal roundOff)
    {
        EnsureDraft();
        EnsurePos();
        EnsureUnpaid();

        if (decimal.Round(roundOff, Invoice.PosMoneyScale) != roundOff)
        {
            throw new InvalidOperationException("A round-off is whole paisa.");
        }

        if (_lines.Sum(x => x.LineTotal) + roundOff < 0m)
        {
            throw new InvalidOperationException("A round-off cannot take a refund below zero.");
        }

        RoundOff = roundOff;
    }

    /// <summary>
    /// Phase 63 -- records what is handed back. <paramref name="requiredPayout"/> is the part of the
    /// refund the customer does not still owe on the sale (the caller reads that from the one reader
    /// of what is owed); the payouts must come to exactly that. Anything paid out beyond it would be
    /// cash for a bill nobody paid, and anything short of it would leave the customer in credit for
    /// money they were owed back (phase-63-status.md Decision D).
    /// </summary>
    public void PayOut(IReadOnlyCollection<Invoice.TenderInput> payouts, decimal requiredPayout)
    {
        EnsureDraft();
        EnsurePos();
        EnsureUnpaid();

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("A refund needs at least one line before it is paid out.");
        }

        if (requiredPayout < 0m || requiredPayout > GrandTotal)
        {
            throw new InvalidOperationException(
                $"A refund of {GrandTotal:0.00} cannot pay out {requiredPayout:0.00}.");
        }

        var built = payouts
            .Select(x => CreditNotePayout.Create(Id, x.PaymentModeId, x.Kind, x.AccountId, x.Amount))
            .ToList();

        var paidOut = built.Sum(x => x.Amount);
        if (paidOut != requiredPayout)
        {
            throw new InvalidOperationException(requiredPayout == 0m
                ? $"Nothing is handed back on this refund: all {GrandTotal:0.00} comes off what the customer still owes."
                : $"Hand back exactly {requiredPayout:0.00}; the payouts come to {paidOut:0.00}.");
        }

        _payouts.AddRange(built);
        _paid = true;
    }

    // Not persisted, for Invoice._settled's reason: it only stops a second PayOut on the in-memory draft.
    private bool _paid;

    private void EnsurePos()
    {
        if (Channel != SalesChannel.Pos)
        {
            throw new InvalidOperationException("Only a refund at a till carries a service charge, a round-off or payouts.");
        }
    }

    private void EnsureUnpaid()
    {
        if (_paid)
        {
            throw new InvalidOperationException("This refund has already been paid out.");
        }
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

    public void AddLine(
        Guid productId, decimal quantity, decimal rate, VatRate vatRate, decimal discountPct,
        Guid? unitId, decimal conversionFactor, Guid? batchId = null)
    {
        EnsureDraft();

        if (Channel == SalesChannel.Pos)
        {
            throw new InvalidOperationException("A till refund's lines carry a service charge rate; add them with AddPosLine.");
        }

        if (quantity <= 0 || rate < 0)
        {
            throw new InvalidOperationException("A credit note line needs a positive Quantity and a non-negative Rate.");
        }

        EnsureValidDiscountPct(discountPct);

        _lines.Add(CreditNoteLine.Create(
            Id, productId, quantity, rate, vatRate, discountPct, DiscountPct, batchId, unitId,
            conversionFactor));
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

        // Phase 39 -- sanitised here rather than in the handler, so that no future caller inside the
        // Domain can store markup that skipped the gate. RichText.Sanitize also collapses
        // "<p><br></p>" to null, which is what the editor emits for an empty field.
        Terms = RichText.Sanitize(terms);
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

    /// <summary>
    /// Phase 32 -- sets the billing location this document is raised from. Draft-only, exactly like
    /// <see cref="SetCurrency"/> beside it and for a related reason: an Approved document's location
    /// is what its numbering pool was drawn from (see DocumentNumberingRule.LocationWiseNumbering)
    /// and what every location-filtered report has already counted it under, so moving it afterwards
    /// would silently restate both.
    ///
    /// <para>Accepts null -- a tenant whose <see cref="Domain.Tenancy.LocationScopeMode"/> excludes
    /// this document type simply never sets one, which is why the column is nullable rather than
    /// defaulted to HeadOffice at the aggregate level. The Create handler resolves the default.</para>
    /// </summary>
    public void SetLocation(Guid? locationId)
    {
        EnsureDraft();
        LocationId = locationId;
    }

    /// <summary>Phase 44 -- fills in a billing location that was never set, for
    /// <c>BackfillDocumentLocationsCommand</c>. Allowed after Approve, unlike <see cref="SetLocation"/>,
    /// and refuses to move one that is already set. See <c>Sales.Invoice.BackfillLocation</c> for the
    /// full reasoning.</summary>
    public void BackfillLocation(Guid locationId)
    {
        if (LocationId is not null)
        {
            throw new InvalidOperationException(
                "This document already has a billing location; the backfill only fills in a missing one.");
        }

        LocationId = locationId;
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
