using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreateLeadSource;

public sealed record CreateLeadSourceCommand(Guid OrganizationId, string Name)
    : IRequest<CreateLeadSourceResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.LeadSourceManage;
}

public sealed record CreateLeadSourceResult(Guid Id, string Name);
