using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.GetQuotation;

public sealed record GetQuotationQuery(Guid OrganizationId, Guid Id)
    : IRequest<QuotationDetailDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.QuotationView;
}

public sealed record QuotationLineDto(
    Guid Id, Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct, decimal Amount, decimal VatAmount);

public sealed record QuotationDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    string Code,
    DateOnly Date,
    DateOnly? ExpiryDate,
    string? Reference,
    QuotationStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    decimal DiscountPct,
    Guid? CustomStatusId,
    IReadOnlyList<QuotationLineDto> Lines);
