using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.InviteUser;

public sealed record InviteUserCommand(Guid OrganizationId, string Email, MembershipRole Role)
    : IRequest<InviteUserResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.OrganizationInviteUser;
}

public sealed record InviteUserResult(Guid MembershipId, string Email, MembershipRole Role);
