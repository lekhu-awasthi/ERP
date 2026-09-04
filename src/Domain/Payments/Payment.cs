using ErpApp.Domain.Common;

namespace ErpApp.Domain.Payments;

/// <summary>
/// Unifies Customer Payment/Supplier Payment/Quick Payment/Quick Receipt (architecture-spec.md
/// §4.6, confirmed same underlying shape). This phase only ever constructs Direction=Received
/// (Customer Payment) -- CreatePaymentCommand hardcodes it; Supplier Payment (Direction=Paid) is
/// Phase 6's near-zero-new-code reuse. AccountId is the cash/bank Account the money moved
/// through, same "Select Account" picker JournalVoucher/CashTransfer already use.
///
/// Approve() requires Allocations to sum to at most Amount (relaxed from "exactly Amount" in
/// Phase 17 -- see phase-17-status.md decision #1): a zero- or partially-allocated Payment can now
/// be Approved, which is what Quick Payment/Quick Receipt (no Contact-tied obligation to allocate
/// against) and the Allocate Customer/Supplier Payment screens (which list exactly these
/// under/un-allocated Approved Payments as sources to apply later) both require. Over-allocation is
/// still rejected -- Allocations can never exceed Amount. The client still fills allocations
/// (GetDefaultPaymentAllocationsQuery's FIFO-oldest-first suggestion, manually overridable) when
/// there's a specific Invoice/PurchaseBill to net against; Quick Payment/Receipt skips that
/// suggestion step entirely.
///
/// AttachAllocations exists because PaymentAllocation.SourceId is polymorphic (decision #2) and so
/// can no longer be an EF-navigable child collection scoped to just the Payments table (a real FK
/// constraint would reject JournalVoucher-sourced rows) -- handlers query PaymentAllocations by
/// (SourceType=Payment, SourceId=this.Id) themselves and hydrate the aggregate before calling any
/// method that reads Allocations.
/// </summary>
public sealed class Payment
{
    public const string DraftCode = "DRAFT";

    private readonly List<PaymentAllocation> _allocations = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public PaymentDirection Direction { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public Guid? PaymentModeId { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string? Reference { get; private set; }
    public PaymentStatus Status { get; private set; }
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

    public IReadOnlyList<PaymentAllocation> Allocations => _allocations;

    private Payment()
    {
    }

    public static Payment Create(
        Guid organizationId,
        Guid contactId,
        PaymentDirection direction,
        DateOnly date,
        Guid? paymentModeId,
        Guid accountId,
        decimal amount,
        string? reference)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("A payment's Amount must be greater than zero.");
        }

        return new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Direction = direction,
            Code = DraftCode,
            Date = date,
            PaymentModeId = paymentModeId,
            AccountId = accountId,
            Amount = amount,
            Reference = reference,
            Status = PaymentStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateHeader(Guid contactId, DateOnly date, Guid? paymentModeId, Guid accountId, decimal amount, string? reference)
    {
        EnsureDraft();

        if (amount <= 0)
        {
            throw new InvalidOperationException("A payment's Amount must be greater than zero.");
        }

        ContactId = contactId;
        Date = date;
        PaymentModeId = paymentModeId;
        AccountId = accountId;
        Amount = amount;
        Reference = reference;
    }

    public void AddAllocation(DocumentType targetDocumentType, Guid targetDocumentId, decimal amount)
    {
        EnsureDraft();
        _allocations.Add(PaymentAllocation.Create(DocumentType.Payment, Id, targetDocumentType, targetDocumentId, amount));
    }

    public void ClearAllocations()
    {
        EnsureDraft();
        _allocations.Clear();
    }

    /// <summary>DB-load plumbing, not a domain action -- see the class doc comment. Replaces
    /// whatever's currently in the in-memory collection with what the caller just loaded from
    /// PaymentAllocations.</summary>
    public void AttachAllocations(IEnumerable<PaymentAllocation> allocations)
    {
        _allocations.Clear();
        _allocations.AddRange(allocations);
    }

    /// <summary>
    /// Phase 17 -- the Allocate Customer/Supplier Payment screens' own write action: apply more of
    /// an already-Approved (and still under-allocated, per decision #1) Payment's remaining
    /// Balance against a target document, without touching Status/Code/ApprovedAt. Distinct from
    /// AddAllocation (Draft-only, used while a Payment is still being composed) -- this is the
    /// counterpart for a Payment that's already posted to GL and simply has room left.
    /// </summary>
    public void AllocateFurther(DocumentType targetDocumentType, Guid targetDocumentId, decimal amount)
    {
        EnsureApproved();

        if (_allocations.Sum(x => x.Amount) + amount > Amount)
        {
            throw new InvalidOperationException("A payment's allocations cannot exceed its Amount.");
        }

        _allocations.Add(PaymentAllocation.Create(DocumentType.Payment, Id, targetDocumentType, targetDocumentId, amount));
    }

    public void Approve(Guid approvedByUserId, string code)
    {
        EnsureDraft();

        if (_allocations.Sum(x => x.Amount) > Amount)
        {
            throw new InvalidOperationException("A payment's allocations cannot exceed its Amount.");
        }

        Status = PaymentStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    /// <summary>Voiding releases every allocation implicitly -- every outstanding-amount
    /// computation in this codebase (GetDefaultPaymentAllocationsQuery, ContactAgeingSummary/
    /// StatementQuery) already filters its "already allocated" join to
    /// Payment.Status==Approved, so a Void payment's Allocations rows stop counting the instant
    /// this flips, with no separate release step needed (roadmap Phase 16a).</summary>
    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = PaymentStatus.Void;
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
        if (Status != PaymentStatus.Draft)
        {
            throw new InvalidOperationException("This payment is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != PaymentStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved payment can be voided.");
        }
    }
}
