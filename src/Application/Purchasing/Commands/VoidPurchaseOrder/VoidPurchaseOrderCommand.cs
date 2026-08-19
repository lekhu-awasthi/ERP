using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.VoidPurchaseOrder;

public sealed record VoidPurchaseOrderCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidPurchaseOrderResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.PurchaseOrderVoid;
    public DocumentType LockDateDocumentType => DocumentType.PurchaseOrder;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidPurchaseOrderResult(Guid Id, string Code, PurchaseOrderStatus Status, DateTimeOffset? VoidedAt);
