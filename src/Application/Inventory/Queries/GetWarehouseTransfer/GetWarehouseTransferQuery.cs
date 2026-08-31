using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.GetWarehouseTransfer;

public sealed record GetWarehouseTransferQuery(Guid OrganizationId, Guid Id)
    : IRequest<WarehouseTransferDetailDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.WarehouseTransferView;

    // Phase 20f (FR-2.6): moving stock between warehouses needs both entitlements -- the
    // inventory tracking that gives the movement meaning, and more than one warehouse to
    // move it between. The only requests in this codebase requiring two features.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory, TenantFeature.MultipleWarehouses];
}

public sealed record WarehouseTransferLineDto(Guid Id, Guid ProductId, decimal Quantity);

public sealed record WarehouseTransferDetailDto(
    Guid Id,
    Guid OrganizationId,
    string Code,
    DateOnly Date,
    string? Reference,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    WarehouseTransferStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<WarehouseTransferLineDto> Lines);
