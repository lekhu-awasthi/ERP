using ErpApp.Application.Common.Security;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreateAlertDefinition;

/// <summary>
/// Roadmap Phase 20e / FR-11.1. ScheduleTime is tenant-local (Nepal) wall-clock time, matching the
/// reference product's own picker -- see AlertDefinition's remarks.
/// </summary>
public sealed record CreateAlertDefinitionCommand(
    Guid OrganizationId,
    string Name,
    AlertMedium Medium,
    AlertType AlertType,
    string Recipients,
    AlertScheduleFrequency Frequency,
    TimeOnly ScheduleTime)
    : IRequest<CreateAlertDefinitionResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AlertDefinitionManage;
}

public sealed record CreateAlertDefinitionResult(
    Guid Id,
    string Name,
    AlertMedium Medium,
    AlertType AlertType,
    string Recipients,
    AlertScheduleFrequency Frequency,
    TimeOnly ScheduleTime,
    bool IsActive);
