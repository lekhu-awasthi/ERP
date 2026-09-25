using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.VoidDeliveryNote;

public sealed record VoidDeliveryNoteCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidDeliveryNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.DeliveryNoteVoid;
    public DocumentType LockDateDocumentType => DocumentType.DeliveryNote;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidDeliveryNoteResult(Guid Id, string Code, DeliveryNoteStatus Status, DateTimeOffset? VoidedAt);
