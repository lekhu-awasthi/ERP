using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.UpdateMembershipRole;

public sealed class UpdateMembershipRoleCommandHandler(IAppDbContext db) : IRequestHandler<UpdateMembershipRoleCommand, Unit>
{
    public async Task<Unit> Handle(UpdateMembershipRoleCommand request, CancellationToken cancellationToken)
    {
        var membership = await db.OrganizationMemberships.SingleOrDefaultAsync(
            m => m.Id == request.MembershipId && m.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Membership not found.");

        if (membership.Status != MembershipStatus.Accepted)
        {
            throw new ConflictException("Only an accepted member's role can be reassigned.");
        }

        var roleIsValid = await db.Roles.AnyAsync(
            r => r.Id == request.RoleId && (r.OrganizationId == null || r.OrganizationId == request.OrganizationId),
            cancellationToken);

        if (!roleIsValid)
        {
            throw new NotFoundException("Role not found.");
        }

        membership.ReassignRole(request.RoleId);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
