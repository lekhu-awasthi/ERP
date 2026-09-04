using ErpApp.Domain.Common;

namespace ErpApp.Domain.Accounting;

/// <summary>
/// Simplified UI over JournalVoucher (architecture-spec.md §4.7) -- one FromAccountId, N
/// (ToAccountId, Amount) destination rows (fan-out confirmed live in the reference product).
/// Internally still posts as one balanced multi-line GL entry through the same
/// IGlPostingRule&lt;CashTransfer&gt;/GlJournalEntry.Post() path JournalVoucher uses -- see
/// CashTransferPostingRule (Application layer), not a parallel posting path.
///
/// Same Draft->Approve/RowVersion/encapsulated-Lines shape as JournalVoucher; see that type's doc
/// comment for the shared rationale.
/// </summary>
public sealed class CashTransfer
{
    public const string DraftCode = "DRAFT";

    private readonly List<CashTransferLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public Guid FromAccountId { get; private set; }
    public CashTransferStatus Status { get; private set; }
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

    public IReadOnlyList<CashTransferLine> Lines => _lines;

    private CashTransfer()
    {
    }

    public static CashTransfer Create(Guid organizationId, DateOnly date, string? reference, Guid fromAccountId)
    {
        return new CashTransfer
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            FromAccountId = fromAccountId,
            Status = CashTransferStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateHeader(DateOnly date, string? reference, Guid fromAccountId)
    {
        EnsureDraft();
        Date = date;
        Reference = reference;
        FromAccountId = fromAccountId;
    }

    public void AddLine(Guid toAccountId, decimal amount)
    {
        EnsureDraft();

        if (amount <= 0)
        {
            throw new InvalidOperationException("Each cash transfer destination line must have an Amount greater than zero.");
        }

        _lines.Add(CashTransferLine.Create(Id, toAccountId, amount));
    }

    public void ClearLines()
    {
        EnsureDraft();
        _lines.Clear();
    }

    public void Approve(Guid approvedByUserId, string code)
    {
        EnsureDraft();

        if (_lines.Count < 1)
        {
            throw new InvalidOperationException("A cash transfer needs at least one destination line to be approved.");
        }

        if (_lines.Any(x => x.ToAccountId == FromAccountId))
        {
            throw new InvalidOperationException("A cash transfer's destination accounts must differ from its From account.");
        }

        Status = CashTransferStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = CashTransferStatus.Void;
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
        if (Status != CashTransferStatus.Draft)
        {
            throw new InvalidOperationException("This cash transfer is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != CashTransferStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved cash transfer can be voided.");
        }
    }
}
