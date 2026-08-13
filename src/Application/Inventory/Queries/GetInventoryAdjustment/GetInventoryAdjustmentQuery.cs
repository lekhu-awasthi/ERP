using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.GetInventoryAdjustment;

public sealed record GetInventoryAdjustmentQuery(Guid OrganizationId, Guid Id)
    : IRequest<InventoryAdjustmentDetailDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentView;
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
