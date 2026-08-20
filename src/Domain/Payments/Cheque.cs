using ErpApp.Domain.Common;

namespace ErpApp.Domain.Payments;

/// <summary>
/// Phase 17 (architecture-spec.md §4.6, docs/phase-17-status.md decisions #4-#6) -- a physical
/// cheque linked to the Payment it was recorded against (PaymentMode.RequiresChequeDetails ==
/// true). Direction reuses Payments.PaymentDirection (Received = Cheque Received, Paid = Cheque
/// Issued -- same axis, same naming the rest of this bounded context already uses). AccountId is
/// denormalized from the linked Payment's own AccountId at Create time (live-confirmed the Cheque
/// Register's "Bank"/"Account" column shows the same value the Payment's own "Deposited To"/"Paid
/// From" account picker set) so listing cheques needs no join back to Payments.
///
/// ChequeNo is a plain user-entered string, not run through IDocumentNumberGenerator -- a physical
/// cheque's number is bank-assigned, not ERP-generated (decision #5).
///
/// No status transition here ever touches GL -- decision #4 found no live-confirmable evidence
/// that Bounced auto-reverses anything; the safe default is Bounced marks this Cheque (and, by
/// extension, its linked Payment) for manual follow-up, and an actual reversal only happens through
/// the linked Payment's own existing Void action (GlJournalEntry.PostReversalOf, unchanged from
/// Phase 16a).
/// </summary>
public sealed class Cheque
{
    private static readonly Dictionary<ChequeStatus, ChequeStatus[]> AllowedTransitions = new()
    {
        [ChequeStatus.Pending] = [ChequeStatus.Deposited, ChequeStatus.Cleared, ChequeStatus.Bounced, ChequeStatus.Cancelled],
        [ChequeStatus.Deposited] = [ChequeStatus.Cleared, ChequeStatus.Bounced, ChequeStatus.Cancelled],
        [ChequeStatus.Cleared] = [],
        [ChequeStatus.Bounced] = [],
        [ChequeStatus.Cancelled] = [],
    };

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LinkedPaymentId { get; private set; }
    public PaymentDirection Direction { get; private set; }
    public Guid AccountId { get; private set; }
    public string ChequeNo { get; private set; } = null!;
    public DateOnly ChequeDate { get; private set; }
    public DateOnly? ReceivedDate { get; private set; }
    public decimal Amount { get; private set; }
    public ChequeStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Cheque()
    {
    }

    public static Cheque Create(
        Guid organizationId, Guid linkedPaymentId, PaymentDirection direction, Guid accountId,
        string chequeNo, DateOnly chequeDate, DateOnly? receivedDate, decimal amount)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("A cheque's Amount must be greater than zero.");
        }

        return new Cheque
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            LinkedPaymentId = linkedPaymentId,
            Direction = direction,
            AccountId = accountId,
            ChequeNo = chequeNo,
            ChequeDate = chequeDate,
            ReceivedDate = receivedDate,
            Amount = amount,
            Status = ChequeStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Only while the linked Payment is still Draft (mirrored by this Cheque still being
    /// Pending -- both flip together, never independently, while the Payment is editable).</summary>
    public void UpdateDetails(Guid accountId, string chequeNo, DateOnly chequeDate, DateOnly? receivedDate, decimal amount)
    {
        if (Status != ChequeStatus.Pending)
        {
            throw new InvalidOperationException("Only a Pending cheque's details can be edited.");
        }

        if (amount <= 0)
        {
            throw new InvalidOperationException("A cheque's Amount must be greater than zero.");
        }

        AccountId = accountId;
        ChequeNo = chequeNo;
        ChequeDate = chequeDate;
        ReceivedDate = receivedDate;
        Amount = amount;
    }

    public void TransitionStatus(ChequeStatus newStatus)
    {
        if (!AllowedTransitions[Status].Contains(newStatus))
        {
            throw new InvalidOperationException($"Cannot move a cheque from {Status} to {newStatus}.");
        }

        Status = newStatus;
    }
}
