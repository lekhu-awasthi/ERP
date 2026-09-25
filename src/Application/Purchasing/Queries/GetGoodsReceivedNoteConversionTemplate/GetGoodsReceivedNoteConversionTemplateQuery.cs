using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.GetGoodsReceivedNoteConversionTemplate;

/// <summary>
/// Phase 58 -- the prefill for "Convert to Goods Received Note" on an approved Purchase Order, which
/// is the order's primary banner action once a tenant runs Physical Movement (live 2026-09-24).
/// The <c>GetPurchaseBillConversionTemplateQuery</c> pattern, carrying the currency, location and
/// every line's unit, for phase 35a's rule that a field added to many aggregates owes every
/// prefill between.
/// </summary>
public sealed record GetGoodsReceivedNoteConversionTemplateQuery(Guid OrganizationId, Guid PurchaseOrderId)
    : IRequest<GoodsReceivedNoteConversionTemplateDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.PurchaseOrderView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => PurchaseOrderId;
}

public sealed record GoodsReceivedNoteConversionTemplateDto(
    Guid ContactId,
    DateOnly Date,
    string? Reference,
    DocumentType ReferrerType,
    Guid ReferrerId,
    decimal DiscountPct,
    IReadOnlyList<GoodsReceivedNoteLineInput> Lines,
    string CurrencyCode,
    decimal ExchangeRate,
    Guid? LocationId);
