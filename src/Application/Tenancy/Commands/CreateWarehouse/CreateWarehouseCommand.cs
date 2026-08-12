using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.CreateWarehouse;

public sealed record CreateWarehouseCommand(Guid OrganizationId, string Name)
    : IRequest<CreateWarehouseResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.WarehouseManage;
}

public sealed record CreateWarehouseResult(Guid Id, string Name);
