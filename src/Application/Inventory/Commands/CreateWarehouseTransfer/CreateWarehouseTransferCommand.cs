using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.CreateWarehouseTransfer;

public sealed record CreateWarehouseTransferCommand(
    Guid OrganizationId,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    DateOnly Date,
    string? Reference,
    IReadOnlyList<WarehouseTransferLineInput> Lines)
    : IRequest<CreateWarehouseTransferResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.WarehouseTransferCreate;
}

public sealed record CreateWarehouseTransferResult(Guid Id, string Code, WarehouseTransferStatus Status);
