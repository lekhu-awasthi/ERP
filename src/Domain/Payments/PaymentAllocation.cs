using ErpApp.Domain.Common;

namespace ErpApp.Domain.Payments;

/// <summary>
/// Generic join (architecture-spec.md §3.4/§4.6) -- both sides are polymorphic. TargetDocumentType/
/// TargetDocumentId (unchanged since Phase 5) point at whichever document this credit pays down.
/// SourceType/SourceId (generalized from a hard PaymentId FK -- docs/phase-17-status.md decision
/// #2, implemented as a follow-up once the Allocate screens existed to plug into) point at whichever
/// credit-bearing document this row came from: Payment (Create via Payment.AddAllocation/
/// AllocateFurther) or JournalVoucher (SourceId is the contributing JournalVoucherLine's own Id, not
/// the parent JournalVoucher's -- a JV can have more than one Contact-tagged line, each tracked
/// independently; created directly by ApplyPaymentAllocationCommandHandler, since JournalVoucherLine
/// has no aggregate-root behavior of its own to route through).
///
/// Own table, no DB-level FK on either polymorphic side -- same "indexed, not FK-constrained"
/// treatment the Target side already used, now applied symmetrically: a real FK constraint on
/// SourceId would reject whichever source type it *doesn't* point at, since it names rows in two
/// different tables depending on SourceType.
/// </summary>
public sealed class PaymentAllocation
{
    public Guid Id { get; private set; }
    public DocumentType SourceType { get; private set; }
    public Guid SourceId { get; private set; }
    public DocumentType TargetDocumentType { get; private set; }
    public Guid TargetDocumentId { get; private set; }
    public decimal Amount { get; private set; }

    private PaymentAllocation()
    {
    }

    public static PaymentAllocation Create(
        DocumentType sourceType, Guid sourceId, DocumentType targetDocumentType, Guid targetDocumentId, decimal amount)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("A payment allocation's Amount must be greater than zero.");
        }

        return new PaymentAllocation
        {
            Id = Guid.NewGuid(),
            SourceType = sourceType,
            SourceId = sourceId,
            TargetDocumentType = targetDocumentType,
            TargetDocumentId = targetDocumentId,
            Amount = amount,
        };
    }
}
