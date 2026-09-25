using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.ApproveDeliveryNote;

/// <summary>Phase 58. <paramref name="OverrideWarning"/> is the Invoice's own Warn-and-continue flag
/// (phase 7), for the same dialog: the reference product raises its "Negative Stock Balance" dialog
/// on a Delivery Note exactly as on an Invoice. Not metered -- no accounting entry is affected.</summary>
public sealed record ApproveDeliveryNoteCommand(Guid OrganizationId, Guid Id, bool OverrideWarning = false)
    : IRequest<ApproveDeliveryNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.DeliveryNoteApprove;
    public DocumentType LockDateDocumentType => DocumentType.DeliveryNote;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveDeliveryNoteResult(Guid Id, string Code, DeliveryNoteStatus Status, DateTimeOffset? ApprovedAt);
