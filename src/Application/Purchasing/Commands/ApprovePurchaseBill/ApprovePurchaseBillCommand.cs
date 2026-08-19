using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;

public sealed record ApprovePurchaseBillCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApprovePurchaseBillResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.PurchaseBillApprove;
    public DocumentType LockDateDocumentType => DocumentType.PurchaseBill;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApprovePurchaseBillResult(Guid Id, string Code, PurchaseBillStatus Status, DateTimeOffset? ApprovedAt);
