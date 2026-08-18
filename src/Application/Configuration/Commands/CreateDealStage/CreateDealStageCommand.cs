using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreateDealStage;

public sealed record CreateDealStageCommand(Guid OrganizationId, string Name, int SortOrder, string? Color)
    : IRequest<CreateDealStageResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.DealStageManage;
}

public sealed record CreateDealStageResult(Guid Id, string Name, int SortOrder, string? Color);
