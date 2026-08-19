using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.GetOrganizationLockDate;

public sealed record GetOrganizationLockDateQuery(Guid OrganizationId)
    : IRequest<GetOrganizationLockDateResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.OrganizationLockDateManage;
}

public sealed record GetOrganizationLockDateResult(Guid OrganizationId, DateOnly? LockDate);
