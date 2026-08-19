using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.UpdatePurchaseOrder;

public sealed record UpdatePurchaseOrderCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, string? Reference,
    IReadOnlyList<PurchaseOrderLineInput> Lines)
    : IRequest<UpdatePurchaseOrderResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.PurchaseOrderEdit;
}

public sealed record UpdatePurchaseOrderResult(Guid Id, string Code, PurchaseOrderStatus Status);
