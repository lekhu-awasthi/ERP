using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.ListWarehouseTransfers;

public sealed record ListWarehouseTransfersQuery(Guid OrganizationId, WarehouseTransferStatus? Status)
    : IRequest<IReadOnlyList<WarehouseTransfer>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.WarehouseTransferView;
}
