using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Queries.ListAllocatablePayments;

/// <summary>
/// Backs the Allocate Customer/Supplier Payment screens (FR-5.12/FR-6.12, docs/phase-17-status.md
/// decisions #2/#8). Two credit sources, generalized via PaymentAllocation's polymorphic
/// SourceType/SourceId (decision #2): Approved Payments with Balance > 0 (unchanged since Phase 17),
/// and Approved JournalVouchers' own Contact-tagged lines with Balance > 0 (new). ShowAllocated=false
/// (Unallocated tab, default) lists rows with Balance > 0; ShowAllocated=true (Allocated tab) lists
/// rows with Balance == 0 (fully applied).
/// </summary>
public sealed record ListAllocatablePaymentsQuery(
    Guid OrganizationId,
    PaymentDirection Direction,
    bool ShowAllocated = false,
    Guid? ContactId = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<AllocatablePaymentDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PaymentEdit;
}

/// <summary>
/// Id is what the client submits back as ApplyPaymentAllocationCommand.SourceId -- the Payment's own
/// Id when SourceType=Payment, or the contributing JournalVoucherLine's own Id when
/// SourceType=JournalVoucher. ParentDocumentId is only populated for SourceType=JournalVoucher (the
/// line's own parent JournalVoucher Id -- ApplyPaymentAllocationCommand needs it for the lock-date
/// check, since a line isn't itself a lock-date-resolvable document type).
/// </summary>
public sealed record AllocatablePaymentDto(
    DocumentType SourceType, Guid Id, Guid? ParentDocumentId, string Code, DateOnly Date,
    Guid ContactId, string ContactName, decimal Amount, decimal Allocated, decimal Balance);
