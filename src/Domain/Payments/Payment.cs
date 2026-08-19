using ErpApp.Domain.Common;

namespace ErpApp.Domain.Payments;

/// <summary>
/// Unifies Customer Payment/Supplier Payment/Quick Payment/Quick Receipt (architecture-spec.md
/// §4.6, confirmed same underlying shape). This phase only ever constructs Direction=Received
/// (Customer Payment) -- CreatePaymentCommand hardcodes it; Supplier Payment (Direction=Paid) is
/// Phase 6's near-zero-new-code reuse. AccountId is the cash/bank Account the money moved
/// through, same "Select Account" picker JournalVoucher/CashTransfer already use.
///
/// Approve() requires Allocations to sum to exactly Amount -- v1 doesn't model an unallocated
/// "advance from customer" remainder (see phase-5-status.md's scope decisions); the client fills
/// allocations (GetDefaultPaymentAllocationsQuery's FIFO-oldest-first suggestion, manually
/// overridable) until the full Amount is accounted for.
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

        if (amount <= 0)
        {
            throw new InvalidOperationException("A payment allocation's Amount must be greater than zero.");
        }

        _allocations.Add(PaymentAllocation.Create(Id, targetDocumentType, targetDocumentId, amount));
    }

    public void ClearAllocations()
    {
        EnsureDraft();
        _allocations.Clear();
    }

    public void Approve(Guid approvedByUserId, string code)
    {
        EnsureDraft();

        if (_allocations.Count == 0 || _allocations.Sum(x => x.Amount) != Amount)
        {
            throw new InvalidOperationException("A payment's allocations must add up to exactly its Amount to be approved.");
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
