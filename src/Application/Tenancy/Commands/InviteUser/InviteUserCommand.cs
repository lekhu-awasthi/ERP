using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.InviteUser;

public sealed record InviteUserCommand(Guid OrganizationId, string Email, MembershipRole Role)
    : IRequest<InviteUserResult>;

public sealed record InviteUserResult(Guid MembershipId, string Email, MembershipRole Role);
