using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;

public sealed record CreateUnitOfMeasurementCommand(Guid OrganizationId, string Name, string ShortName)
    : IRequest<CreateUnitOfMeasurementResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.UnitOfMeasurementManage;
}

public sealed record CreateUnitOfMeasurementResult(Guid Id, string Name, string ShortName);
