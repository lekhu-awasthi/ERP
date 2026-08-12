using ErpApp.Domain.Common;

namespace ErpApp.Application.Payments;

/// <summary>Shared allocation-input shape for Create/UpdatePayment and
/// GetDefaultPaymentAllocationsQuery's suggestion -- the client always resubmits its whole
/// current allocation-table state, same JournalVoucherLineInput precedent.</summary>
public sealed record PaymentAllocationInput(DocumentType TargetDocumentType, Guid TargetDocumentId, decimal Amount);
