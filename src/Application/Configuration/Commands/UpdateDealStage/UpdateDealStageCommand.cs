using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdateDealStage;

public sealed record UpdateDealStageCommand(Guid OrganizationId, Guid Id, string Name, int SortOrder, string? Color, bool IsActive)
    : IRequest<UpdateDealStageResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.DealStageManage;
}

public sealed record UpdateDealStageResult(Guid Id, string Name, int SortOrder, string? Color, bool IsActive);
