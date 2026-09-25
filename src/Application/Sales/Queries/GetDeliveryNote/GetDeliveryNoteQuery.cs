using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.GetDeliveryNote;

public sealed record GetDeliveryNoteQuery(Guid OrganizationId, Guid Id)
    : IRequest<DeliveryNoteDetailDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.DeliveryNoteView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => Id;
}

/// <summary>The Sales Order line DTO shape, with phase 52's unit and frozen factor.</summary>
public sealed record DeliveryNoteLineDto(
    Guid Id, Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct, decimal Amount,
    decimal VatAmount, Guid? UnitId, string? UnitName, decimal ConversionFactor);

/// <param name="ReferrerCode">The Sales Order's number, for the detail page's link to it.</param>
public sealed record DeliveryNoteDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    Guid WarehouseId,
    string Code,
    DateOnly Date,
    DateOnly ExpectedDeliveryDate,
    string? Reference,
    string? TrackingNo,
    string? ShippingAddress,
    DeliveryNoteStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? VoidedAt,
    DateTimeOffset CreatedAt,
    decimal DiscountPct,
    Guid? CustomStatusId,
    string? Terms,
    DocumentType? ReferrerType,
    Guid? ReferrerId,
    string? ReferrerCode,
    IReadOnlyList<DeliveryNoteLineDto> Lines,
    string CurrencyCode,
    decimal ExchangeRate,
    Guid? LocationId);
