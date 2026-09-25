using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Sales.Queries.GetDeliveryNoteConversionTemplate;

/// <summary>
/// Phase 58 -- the prefill for "Convert to Delivery Note" on an approved Sales Order, the order's
/// primary banner action under Physical Movement (live 2026-09-24). This is the Sales Order's first
/// conversion in this codebase; the reference product's other one, Convert to Invoice, is not built
/// here and is not this phase's.
/// </summary>
public sealed record GetDeliveryNoteConversionTemplateQuery(Guid OrganizationId, Guid SalesOrderId)
    : IRequest<DeliveryNoteConversionTemplateDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.SalesOrderView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => SalesOrderId;
}

/// <param name="ExpectedDeliveryDate">The order's own delivery date when it has one, else the day
/// after today -- the live form's default.</param>
public sealed record DeliveryNoteConversionTemplateDto(
    Guid ContactId,
    DateOnly Date,
    DateOnly ExpectedDeliveryDate,
    string? Reference,
    DocumentType ReferrerType,
    Guid ReferrerId,
    decimal DiscountPct,
    string? Terms,
    IReadOnlyList<DeliveryNoteLineInput> Lines,
    string CurrencyCode,
    decimal ExchangeRate,
    Guid? LocationId);
