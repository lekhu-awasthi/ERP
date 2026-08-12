using ErpApp.Domain.Common;

namespace ErpApp.Domain.Payments;

/// <summary>
/// Generic join (architecture-spec.md §3.4/§4.6) -- TargetDocumentType/TargetDocumentId point at
/// whichever document this Payment pays down (Invoice this phase; PurchaseBill/JournalVoucher/
/// Expense join in later phases per the scan's confirmed Allocate Payment screen). Own table,
/// created only via Payment.AddAllocation.
/// </summary>
public sealed class PaymentAllocation
{
    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public DocumentType TargetDocumentType { get; private set; }
    public Guid TargetDocumentId { get; private set; }
    public decimal Amount { get; private set; }

    private PaymentAllocation()
    {
    }

    internal static PaymentAllocation Create(Guid paymentId, DocumentType targetDocumentType, Guid targetDocumentId, decimal amount)
    {
        return new PaymentAllocation
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            TargetDocumentType = targetDocumentType,
            TargetDocumentId = targetDocumentId,
            Amount = amount,
        };
    }
}
