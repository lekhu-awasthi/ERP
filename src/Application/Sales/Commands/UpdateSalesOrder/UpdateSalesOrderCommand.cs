using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.UpdateSalesOrder;

public sealed record UpdateSalesOrderCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, DateOnly? DeliveryDate, string? Reference,
    IReadOnlyList<SalesOrderLineInput> Lines)
    : IRequest<UpdateSalesOrderResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.SalesOrderEdit;
}

public sealed record UpdateSalesOrderResult(Guid Id, string Code, SalesOrderStatus Status);
