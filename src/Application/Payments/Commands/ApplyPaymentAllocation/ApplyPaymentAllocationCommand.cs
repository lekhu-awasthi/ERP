using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Payments.Commands.ApplyPaymentAllocation;

/// <summary>
/// The Allocate Customer/Supplier Payment screens' own write action (FR-5.12/FR-6.12,
/// docs/phase-17-status.md decisions #2/#8) -- applies more of an already-Approved, still
/// under-allocated credit's remaining Balance against a target Invoice/PurchaseBill.
///
/// SourceType/SourceId (decision #2) generalize what "the source being applied" means:
/// SourceType=Payment -> SourceId is the Payment's own Id (Payment.AllocateFurther enforces the
/// invariant); SourceType=JournalVoucher -> SourceId is the contributing JournalVoucherLine's own
/// Id (a JV can have more than one Contact-tagged line). ParentDocumentId is only meaningful for
/// SourceType=JournalVoucher -- LockDateBehavior resolves a JournalVoucher's Date from the
/// JournalVoucher's own Id, not a line's, so this carries the parent explicitly rather than making
/// the pipeline behavior look it up.
/// </summary>
public sealed record ApplyPaymentAllocationCommand(
    Guid OrganizationId, DocumentType SourceType, Guid SourceId, Guid? ParentDocumentId,
    DocumentType TargetDocumentType, Guid TargetDocumentId, decimal Amount)
    : IRequest<ApplyPaymentAllocationResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.PaymentEdit;
    public DocumentType LockDateDocumentType => SourceType;
    public Guid LockDateDocumentId => SourceType == DocumentType.JournalVoucher && ParentDocumentId is { } id ? id : SourceId;
}

public sealed record ApplyPaymentAllocationResult(Guid Id, decimal Amount, decimal Allocated, decimal Balance);
