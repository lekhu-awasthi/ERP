using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.GetPurchaseOrder;

public sealed record GetPurchaseOrderQuery(Guid OrganizationId, Guid Id)
    : IRequest<PurchaseOrderDetailDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PurchaseOrderView;
}

public sealed record PurchaseOrderLineDto(
    Guid Id, Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct, decimal Amount, decimal VatAmount);

public sealed record PurchaseOrderDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    string Code,
    DateOnly Date,
    string? Reference,
    PurchaseOrderStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    decimal DiscountPct,
    Guid? CustomStatusId,
    string? Terms,
    IReadOnlyList<PurchaseOrderLineDto> Lines,
    // Phase 28 (FR-2.5) -- the document's own currency and its rate to the base currency.
    // Every amount above is denominated in CurrencyCode; the general ledger figures under
    // GlLines are in the base currency, already converted at ExchangeRate.
    string CurrencyCode,
    decimal ExchangeRate);
