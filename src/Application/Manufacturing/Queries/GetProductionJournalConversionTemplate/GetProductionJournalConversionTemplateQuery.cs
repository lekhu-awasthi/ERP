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
    : IRequest<ProductionJournalConversionTemplateDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.ProductionOrderView;

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
    IReadOnlyList<ProductionExpenseLineInput> Expenses);
