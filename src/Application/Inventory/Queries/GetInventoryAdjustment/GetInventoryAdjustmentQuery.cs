using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.GetInventoryAdjustment;

public sealed record GetInventoryAdjustmentQuery(Guid OrganizationId, Guid Id)
    : IRequest<InventoryAdjustmentDetailDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentView;

    // Phase 20f (FR-2.6): the Inventory context is only available to a tenant that opted
    // into Track Inventory. Catalog (Products/Categories/Units) is deliberately NOT gated --
    // live-confirmed that the reference product files those under Inventory in its nav but
    // every tenant needs them. See phase-20f-status.md.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
}

public sealed record InventoryAdjustmentLineDto(
    Guid Id, Guid ProductId, InventoryAdjustmentDirection Direction, decimal Quantity, decimal UnitCost);

public sealed record PostedGlLineDto(Guid Id, Guid AccountId, decimal Debit, decimal Credit);

public sealed record InventoryAdjustmentDetailDto(
    Guid Id,
    Guid OrganizationId,
    string Code,
    DateOnly Date,
    string? Reference,
    Guid WarehouseId,
    InventoryAdjustmentStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<InventoryAdjustmentLineDto> Lines,
    IReadOnlyList<PostedGlLineDto>? GlLines);
