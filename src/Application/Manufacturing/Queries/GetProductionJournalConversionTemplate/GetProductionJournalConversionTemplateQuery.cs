using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.GetProductionJournalConversionTemplate;

/// <summary>
/// architecture-spec.md §3.3's document-conversion pattern, applied a fourth time. Keyed off the
/// <i>source</i> document's View key exactly as GetInvoiceConversionTemplateQuery and
/// GetPurchaseBillConversionTemplateQuery are: the caller must already be able to see the order
/// they are converting.
/// </summary>
public sealed record GetProductionJournalConversionTemplateQuery(Guid OrganizationId, Guid ProductionOrderId)
    : IRequest<ProductionJournalConversionTemplateDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.ProductionOrderView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => ProductionOrderId;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record ProductionJournalConversionTemplateDto(
    DateOnly Date,
    string? Reference,
    Guid ProductId,
    string ProductName,
    decimal OutputQuantity,
    Guid? BillOfMaterialsId,
    string? Notes,
    DocumentType ReferrerType,
    Guid ReferrerId,
    IReadOnlyList<ProductionRawMaterialLineInput> RawMaterials,
    IReadOnlyList<ProductionByProductLineInput> ByProducts,
    IReadOnlyList<ProductionExpenseLineInput> Expenses,
    // Phase 35a -- the source document's billing location, carried into the prefill. Without it a
    // Quotation raised at a branch converted to an Invoice at HeadOffice, silently, because the new
    // form's picker falls back to the tenant default. Same shape as the currency the conversion
    // flow already carries verbatim.
    Guid? LocationId);
