using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.AcceptInvitation;

public sealed class AcceptInvitationCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<AcceptInvitationCommand>
{
    public async Task Handle(AcceptInvitationCommand request, CancellationToken cancellationToken)
    {
        var membership = await db.OrganizationMemberships.SingleOrDefaultAsync(m => m.Id == request.MembershipId, cancellationToken)
            ?? throw new NotFoundException("Invitation not found.");

        if (membership.Status != MembershipStatus.Invited)
        {
            throw new ConflictException("This invitation is no longer pending.");
        }

        var user = await db.Users.SingleAsync(u => u.Id == currentUser.UserId, cancellationToken);

        var isForCurrentUser = membership.UserId == currentUser.UserId
            || (membership.UserId is null && membership.InvitedEmail == user.Email);

        if (!isForCurrentUser)
        {
            throw new ForbiddenException("This invitation was not addressed to you.");
        }

        membership.AcceptAsUser(currentUser.UserId);
        await db.SaveChangesAsync(cancellationToken);
    }
}
