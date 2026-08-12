using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.GetPurchaseBill;

public sealed record GetPurchaseBillQuery(Guid OrganizationId, Guid Id)
    : IRequest<PurchaseBillDetailDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PurchaseBillView;
}

public sealed record PurchaseBillLineDto(
    Guid Id, Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal Amount, decimal VatAmount,
    ExpenditureClassification ExpenditureClassification);

public sealed record PostedGlLineDto(Guid Id, Guid AccountId, decimal Debit, decimal Credit);

public sealed record PurchaseBillDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    Guid WarehouseId,
    string Code,
    DateOnly Date,
    string? Reference,
    string? SupplierInvoiceReference,
    bool IsImport,
    string? ImportCountry,
    DateOnly? ImportDate,
    string? ImportDocumentNo,
    Guid? TdsTypeId,
    decimal TdsAmount,
    PurchaseBillStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DocumentType? ReferrerType,
    Guid? ReferrerId,
    decimal GrandTotal,
    IReadOnlyList<PurchaseBillLineDto> Lines,
    IReadOnlyList<PostedGlLineDto>? GlLines);
