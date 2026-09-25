using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.VoidGoodsReceivedNote;

public sealed record VoidGoodsReceivedNoteCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidGoodsReceivedNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.GoodsReceivedNoteVoid;
    public DocumentType LockDateDocumentType => DocumentType.GoodsReceivedNote;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidGoodsReceivedNoteResult(
    Guid Id, string Code, GoodsReceivedNoteStatus Status, DateTimeOffset? VoidedAt);
