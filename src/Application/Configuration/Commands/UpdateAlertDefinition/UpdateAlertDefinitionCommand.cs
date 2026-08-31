using ErpApp.Application.Common.Security;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdateAlertDefinition;

public sealed record UpdateAlertDefinitionCommand(
    Guid OrganizationId,
    Guid Id,
    string Name,
    AlertMedium Medium,
    AlertType AlertType,
    string Recipients,
    AlertScheduleFrequency Frequency,
    TimeOnly ScheduleTime,
    bool IsActive)
    : IRequest<UpdateAlertDefinitionResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AlertDefinitionManage;
}

public sealed record UpdateAlertDefinitionResult(
    Guid Id,
    string Name,
    AlertMedium Medium,
    AlertType AlertType,
    string Recipients,
    AlertScheduleFrequency Frequency,
    TimeOnly ScheduleTime,
    bool IsActive);
