using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.ProductionPlanning;

/// <summary>
/// Reports &gt; Inventory Report &gt; Production Planning Report. Read live on 2026-09-02, and
/// notably <b>not</b> a period report: it takes a Product and a Quantity to be produced and
/// answers "what do I need, and do I have it?" -- Item Name, Unit Of Measurement, Quantity
/// Required, Quantity Available, Surplus/(Deficiency), for the finished product's BOM raw
/// materials only. The live report's own header states "Multiple Level: No", so the explosion is
/// single-level: a raw material that is itself manufactured is not expanded further.
///
/// <para><see cref="WarehouseId"/> is this build's own addition, and optional. The reference
/// tenant showed one availability figure with no warehouse control, which is what null gives here
/// (stock summed across every warehouse). But our FIFO layers are keyed (ProductId, WarehouseId)
/// and a Production Journal consumes from exactly one warehouse, so a planner who already knows
/// where the run will happen would otherwise be shown a number that cannot be consumed.</para>
/// </summary>
public sealed record ProductionPlanningQuery(
    Guid OrganizationId, Guid ProductId, decimal Quantity, Guid? WarehouseId)
    : IRequest<ProductionPlanningReportDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.ProductionReportView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record ProductionPlanningLineDto(
    Guid ProductId,
    string ProductName,
    string ProductCode,
    string? UnitName,
    decimal QuantityRequired,
    decimal QuantityAvailable,
    decimal Surplus);

public sealed record ProductionPlanningReportDto(
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    Guid? BillOfMaterialsId,
    decimal? BomOutputQuantity,
    bool MultipleLevel,
    IReadOnlyList<ProductionPlanningLineDto> Lines);
