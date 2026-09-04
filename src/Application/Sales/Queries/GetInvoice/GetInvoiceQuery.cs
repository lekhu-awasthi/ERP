using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.GetInvoice;

public sealed record GetInvoiceQuery(Guid OrganizationId, Guid Id)
    : IRequest<InvoiceDetailDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InvoiceView;
}

public sealed record InvoiceLineDto(
    Guid Id, Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct, decimal Amount, decimal VatAmount);

public sealed record PostedGlLineDto(Guid Id, Guid AccountId, decimal Debit, decimal Credit);

public sealed record InvoiceDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    Guid WarehouseId,
    string Code,
    DateOnly Date,
    string? Reference,
    bool IsExport,
    string? ExportCountry,
    string? ExportDeclarationNo,
    DateOnly? ExportDeclarationDate,
    InvoiceStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DocumentType? ReferrerType,
    Guid? ReferrerId,
    decimal DiscountPct,
    decimal GrandTotal,
    string? Terms,
    IReadOnlyList<InvoiceLineDto> Lines,
    IReadOnlyList<PostedGlLineDto>? GlLines,
    // Phase 28 (FR-2.5) -- the document's own currency and its rate to the base currency.
    // Every amount above is denominated in CurrencyCode; the general ledger figures under
    // GlLines are in the base currency, already converted at ExchangeRate.
    string CurrencyCode,
    decimal ExchangeRate);
