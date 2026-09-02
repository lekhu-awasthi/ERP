using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.GetBomTemplate;

/// <summary>
/// The server side of "LOAD BOM" -- the reference product's own explicit, user-invoked template
/// load, which appears on the Production Order/Journal forms only once a Product and an Output
/// Quantity are both set. Given a finished product and the quantity the user intends to make, it
/// returns that product's BOM lines scaled by (OutputQuantity / the BOM's own OutputQuantity).
/// Verified live: a BOM with output 12 and one raw material at 12 returned 24 for an output of 24,
/// its by-product 15 became 30, its 500 expense became 1000, and the by-product's % of Cost stayed
/// at 12 -- percentages are ratios already, so they are the one thing not scaled.
///
/// <para>Permission key is the target document's Create key, not the BOM's View key: this
/// populates a form the caller is about to submit, so it must be no easier to reach than creating
/// the document itself. That is PrintDocumentQuery's and phase-22's inbox-prefill shape -- a
/// prefill query is never a side door around AuthorizationBehavior. It differs from the
/// Quotation/PurchaseOrder conversion templates, which key off the <i>source document's</i> View
/// key because there the source is a real document the caller must already be able to see; a BOM
/// is master data every Member can read anyway.</para>
/// </summary>
public sealed record GetBomTemplateQuery(Guid OrganizationId, Guid ProductId, decimal OutputQuantity)
    : IRequest<BomTemplateDto?>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.ProductionJournalCreate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record BomTemplateRawMaterialDto(Guid ProductId, string ProductName, string ProductCode, decimal Quantity);

public sealed record BomTemplateByProductDto(
    Guid ProductId, string ProductName, string ProductCode, decimal CostAllocationPct, decimal Quantity);

public sealed record BomTemplateExpenseDto(Guid CostTermId, string CostTermName, decimal Amount);

public sealed record BomTemplateDto(
    Guid BillOfMaterialsId,
    decimal BomOutputQuantity,
    decimal OutputQuantity,
    IReadOnlyList<BomTemplateRawMaterialDto> RawMaterials,
    IReadOnlyList<BomTemplateByProductDto> ByProducts,
    IReadOnlyList<BomTemplateExpenseDto> Expenses);
