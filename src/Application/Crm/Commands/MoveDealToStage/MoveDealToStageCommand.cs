using ErpApp.Application.Common.Security;
using ErpApp.Domain.Crm;
using MediatR;

namespace ErpApp.Application.Crm.Commands.MoveDealToStage;

/// <summary>Separate from UpdateDealCommand, mirroring UpdateTaskStatusCommand's own separation of
/// the state-changing action from the general edit -- also the rename the brief called for to
/// avoid colliding with Configuration.Commands.UpdateDealStage (the DealStage *lookup*'s own
/// Update command).</summary>
public sealed record MoveDealToStageCommand(Guid OrganizationId, Guid Id, Guid DealStageId)
    : IRequest<MoveDealToStageResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.DealManage;
}

public sealed record MoveDealToStageResult(Guid Id, Guid DealStageId, DealStatus Status);
