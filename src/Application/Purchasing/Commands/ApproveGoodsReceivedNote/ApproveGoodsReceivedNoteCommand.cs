using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.ApproveGoodsReceivedNote;

/// <summary>Phase 58 -- not an <c>IMeteredTransaction</c>: the subscription meters "transactions in
/// which accounting entry are affected" (phase 41), and a GRN posts no accounting entry.</summary>
public sealed record ApproveGoodsReceivedNoteCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveGoodsReceivedNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.GoodsReceivedNoteApprove;
    public DocumentType LockDateDocumentType => DocumentType.GoodsReceivedNote;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveGoodsReceivedNoteResult(
    Guid Id, string Code, GoodsReceivedNoteStatus Status, DateTimeOffset? ApprovedAt);
