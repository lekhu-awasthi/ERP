using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateSalesOrder;

public sealed record CreateSalesOrderCommand(
    Guid OrganizationId, Guid ContactId, DateOnly Date, DateOnly? DeliveryDate, string? Reference,
    IReadOnlyList<SalesOrderLineInput> Lines)
    : IRequest<CreateSalesOrderResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.SalesOrderCreate;
}

public sealed record CreateSalesOrderResult(Guid Id, string Code, SalesOrderStatus Status);
