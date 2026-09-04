using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.GetSalesOrder;

public sealed record GetSalesOrderQuery(Guid OrganizationId, Guid Id)
    : IRequest<SalesOrderDetailDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SalesOrderView;
}

public sealed record SalesOrderLineDto(
    Guid Id, Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct, decimal Amount, decimal VatAmount);

public sealed record SalesOrderDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    string Code,
    DateOnly Date,
    DateOnly? DeliveryDate,
    string? Reference,
    SalesOrderStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    decimal DiscountPct,
    string? Terms,
    IReadOnlyList<SalesOrderLineDto> Lines);
