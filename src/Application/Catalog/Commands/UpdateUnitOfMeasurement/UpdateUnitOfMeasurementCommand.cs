using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.UpdateUnitOfMeasurement;

public sealed record UpdateUnitOfMeasurementCommand(Guid OrganizationId, Guid Id, string Name, string ShortName, bool IsActive)
    : IRequest<UpdateUnitOfMeasurementResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.UnitOfMeasurementManage;
}

public sealed record UpdateUnitOfMeasurementResult(Guid Id, string Name, string ShortName, bool IsActive);
