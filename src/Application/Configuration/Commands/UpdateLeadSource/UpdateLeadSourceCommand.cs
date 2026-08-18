using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdateLeadSource;

public sealed record UpdateLeadSourceCommand(Guid OrganizationId, Guid Id, string Name, bool IsActive)
    : IRequest<UpdateLeadSourceResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.LeadSourceManage;
}

public sealed record UpdateLeadSourceResult(Guid Id, string Name, bool IsActive);
