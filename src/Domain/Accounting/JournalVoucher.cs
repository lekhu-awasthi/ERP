using ErpApp.Domain.Common;

namespace ErpApp.Domain.Accounting;

/// <summary>
/// The first real ApprovableTransaction (architecture-spec.md §3.2/§4.7): manual Debit/Credit
/// entries against any Account, Draft -> Approve. Code sits at the literal placeholder
/// <see cref="DraftCode"/> until Approve assigns the real IDocumentNumberGenerator-issued number
/// (architecture-spec.md §3.1 -- numbers assigned at Approve, not Create, confirmed live).
///
/// Lines is an encapsulated child collection (private backing field), same mapping shape as
/// Catalog.Product.SecondaryUnits. All mutation (AddLine/ClearLines/UpdateHeader) is Draft-only;
/// Approve() itself only flips Status/ApprovedBy/ApprovedAt/Code and enforces the
/// sum(Debit)==sum(Credit) invariant fast, before the Application-layer handler ever builds a
/// GlJournalEntry via IGlPostingRule -- Domain stays ignorant of the posting-rule abstraction,
/// which is an Application-layer concern (it doesn't need any I/O for JournalVoucher, but will for
/// later document types).
/// </summary>
public sealed class JournalVoucher
{
    public const string DraftCode = "DRAFT";

    private readonly List<JournalVoucherLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public JournalVoucherStatus Status { get; private set; }
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

    public IReadOnlyList<JournalVoucherLine> Lines => _lines;

    private JournalVoucher()
    {
    }

    public static JournalVoucher Create(Guid organizationId, DateOnly date, string? reference)
    {
        return new JournalVoucher
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            Status = JournalVoucherStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateHeader(DateOnly date, string? reference)
    {
        EnsureDraft();
        Date = date;
        Reference = reference;
    }

    public void AddLine(Guid accountId, decimal debit, decimal credit, Guid? contactId = null)
    {
        EnsureDraft();

        if (debit < 0 || credit < 0 || (debit > 0 && credit > 0) || (debit == 0 && credit == 0))
        {
            throw new InvalidOperationException(
                "Each journal voucher line must have exactly one of Debit or Credit greater than zero.");
        }

        _lines.Add(JournalVoucherLine.Create(Id, accountId, debit, credit, contactId));
    }

    /// <summary>Full-replace of the line set -- the simplest correct approach for a client-driven
    /// multi-line editable table where the client always resubmits its whole current state.</summary>
    public void ClearLines()
    {
        EnsureDraft();
        _lines.Clear();
    }

    public void Approve(Guid approvedByUserId, string code)
    {
        EnsureDraft();

        if (_lines.Count < 2)
        {
            throw new InvalidOperationException("A journal voucher needs at least two lines to be approved.");
        }

        if (_lines.Sum(x => x.Debit) != _lines.Sum(x => x.Credit))
        {
            throw new InvalidOperationException(
                "A journal voucher's total Debit must equal its total Credit to be approved.");
        }

        Status = JournalVoucherStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    /// <summary>Terminal -- no edit, re-approve, or un-void afterward (roadmap Phase 16a). The
    /// document keeps its assigned Code; numbers are never recycled.</summary>
    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = JournalVoucherStatus.Void;
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
        if (Status != JournalVoucherStatus.Draft)
        {
            throw new InvalidOperationException("This journal voucher is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != JournalVoucherStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved journal voucher can be voided.");
        }
    }
}
