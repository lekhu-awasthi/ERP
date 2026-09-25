using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.GetGoodsReceivedNote;

public sealed record GetGoodsReceivedNoteQuery(Guid OrganizationId, Guid Id)
    : IRequest<GoodsReceivedNoteDetailDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.GoodsReceivedNoteView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => Id;
}

/// <summary>The <c>PurchaseOrderLineDto</c> shape: phase 52's unit, its short name and the frozen
/// factor, and no converted quantity (it would be a second quantity able to contradict the first).</summary>
public sealed record GoodsReceivedNoteLineDto(
    Guid Id, Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct, decimal Amount,
    decimal VatAmount, Guid? UnitId, string? UnitName, decimal ConversionFactor);

/// <param name="ReferrerCode">The Purchase Order's number, so the detail page can link to it without a
/// second round trip -- the live GRN page shows its PO in the left panel.</param>
public sealed record GoodsReceivedNoteDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    Guid WarehouseId,
    string Code,
    DateOnly Date,
    string? Reference,
    string? TrackingNo,
    GoodsReceivedNoteStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? VoidedAt,
    DateTimeOffset CreatedAt,
    decimal DiscountPct,
    Guid? CustomStatusId,
    DocumentType? ReferrerType,
    Guid? ReferrerId,
    string? ReferrerCode,
    IReadOnlyList<GoodsReceivedNoteLineDto> Lines,
    string CurrencyCode,
    decimal ExchangeRate,
    Guid? LocationId);
