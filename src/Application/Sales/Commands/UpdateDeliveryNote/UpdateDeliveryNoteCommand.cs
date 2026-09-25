using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.UpdateDeliveryNote;

/// <summary>Phase 58 -- edits a Draft Delivery Note. Draft-only, for the reason on
/// <c>UpdateGoodsReceivedNoteCommand</c>; the Sales Order referrer is fixed at create.</summary>
public sealed record UpdateDeliveryNoteCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, Guid WarehouseId, DateOnly Date, DateOnly ExpectedDeliveryDate,
    string? Reference, string? TrackingNo, string? ShippingAddress,
    IReadOnlyList<DeliveryNoteLineInput> Lines, decimal DiscountPct = 0, string? Terms = null)
    : IRequest<UpdateDeliveryNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId, ICurrencyBearingCommand, ILocationBearingCommand, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.DeliveryNoteEdit;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => Id;

    /// <inheritdoc cref="Commands.CreateDeliveryNote.CreateDeliveryNoteCommand.CurrencyCode"/>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }

    /// <inheritdoc cref="Commands.CreateDeliveryNote.CreateDeliveryNoteCommand.LocationId"/>
    public Guid? LocationId { get; init; }

    public DocumentType AuditDocumentType => DocumentType.DeliveryNote;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateDeliveryNoteResult(Guid Id, string Code, DeliveryNoteStatus Status);
