using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.VoidPurchaseBill;

public sealed record VoidPurchaseBillCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidPurchaseBillResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.PurchaseBillVoid;
    public DocumentType LockDateDocumentType => DocumentType.PurchaseBill;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidPurchaseBillResult(Guid Id, string Code, PurchaseBillStatus Status, DateTimeOffset? VoidedAt);
