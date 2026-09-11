using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.GetProductionOrder;

public sealed record GetProductionOrderQuery(Guid OrganizationId, Guid Id)
    : IRequest<ProductionOrderDetailDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.ProductionOrderView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => Id;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record ProductionOrderRawMaterialLineDto(
    Guid Id, Guid ProductId, string ProductName, string ProductCode, string? UnitName, decimal Quantity);

public sealed record ProductionOrderByProductLineDto(
    Guid Id, Guid ProductId, string ProductName, string ProductCode, string? UnitName,
    decimal CostAllocationPct, decimal Quantity);

public sealed record ProductionOrderExpenseLineDto(Guid Id, Guid CostTermId, string CostTermName, decimal Amount);

public sealed record ProductionOrderDetailDto(
    Guid Id,
    string Code,
    DateOnly Date,
    string? Reference,
    Guid ProductId,
    string ProductName,
    string ProductCode,
    string? UnitName,
    decimal OutputQuantity,
    Guid? BillOfMaterialsId,
    string? Notes,
    ProductionOrderStatus Status,
    Guid? ConvertedToProductionJournalId,
    string? ConvertedToProductionJournalCode,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? VoidedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ProductionOrderRawMaterialLineDto> RawMaterials,
    IReadOnlyList<ProductionOrderByProductLineDto> ByProducts,
    IReadOnlyList<ProductionOrderExpenseLineDto> Expenses,
    // Phase 35 -- the billing location this document was raised from, null when the tenant's
    // LocationScopeMode excludes this type. Phase 32 put the column on all 17 types but the header
    // picker on Invoice alone, so only InvoiceDetailDto ever carried it back: a detail query
    // projecting a DTO silently drops a field the aggregate has, and the form would then post the
    // picker's default over a stored location on every edit. Fourteen instances of phase-32's own
    // carried gotcha.
    Guid? LocationId);
