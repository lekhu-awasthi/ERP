using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateDeliveryNote;

/// <summary>Phase 58 -- creates a Draft Delivery Note. <paramref name="ReferrerType"/> /
/// <paramref name="ReferrerId"/> name the Sales Order it delivers, if any; the handler checks the
/// order and marks it delivered.</summary>
public sealed record CreateDeliveryNoteCommand(
    Guid OrganizationId, Guid ContactId, Guid WarehouseId, DateOnly Date, DateOnly ExpectedDeliveryDate,
    string? Reference, string? TrackingNo, string? ShippingAddress,
    IReadOnlyList<DeliveryNoteLineInput> Lines, decimal DiscountPct = 0,
    DocumentType? ReferrerType = null, Guid? ReferrerId = null, string? Terms = null)
    : IRequest<CreateDeliveryNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest, ICurrencyBearingCommand, ILocationBearingCommand
{
    public string PermissionKey => PermissionKeys.DeliveryNoteCreate;

    /// <summary>Phase 28's currency pair -- see <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }

    /// <summary>Phase 32's billing location -- see <see cref="ILocationBearingCommand"/>.</summary>
    public Guid? LocationId { get; init; }

    public DocumentType AuditDocumentType => DocumentType.DeliveryNote;
}

public sealed record CreateDeliveryNoteResult(Guid Id, string Code, DeliveryNoteStatus Status);
