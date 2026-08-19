using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.ApproveInvoice;

/// <summary>OverrideWarning (Phase 7) lets the Angular client resubmit after a
/// StockAvailabilityWarningException without a second round-trip -- architecture-spec.md §3.5's
/// own recommendation for the Warn-and-allow flow. Defaults to false so every existing caller
/// (and every pre-Phase-7 test) keeps behaving exactly as before.</summary>
public sealed record ApproveInvoiceCommand(Guid OrganizationId, Guid Id, bool OverrideWarning = false)
    : IRequest<ApproveInvoiceResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.InvoiceApprove;
    public DocumentType LockDateDocumentType => DocumentType.Invoice;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveInvoiceResult(Guid Id, string Code, InvoiceStatus Status, DateTimeOffset? ApprovedAt);
