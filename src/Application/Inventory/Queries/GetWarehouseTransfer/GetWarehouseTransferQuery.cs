using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.GetWarehouseTransfer;

public sealed record GetWarehouseTransferQuery(Guid OrganizationId, Guid Id)
    : IRequest<WarehouseTransferDetailDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.WarehouseTransferView;
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
