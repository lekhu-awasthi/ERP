using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.UpdateWarehouse;

public sealed record UpdateWarehouseCommand(Guid OrganizationId, Guid Id, string Name, bool IsActive)
    : IRequest<UpdateWarehouseResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.WarehouseManage;
}

public sealed record UpdateWarehouseResult(Guid Id, string Name, bool IsActive);
