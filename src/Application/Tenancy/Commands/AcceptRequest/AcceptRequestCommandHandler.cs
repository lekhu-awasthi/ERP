using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.AcceptRequest;

public sealed class AcceptRequestCommandHandler(IAppDbContext db) : IRequestHandler<AcceptRequestCommand>
{
    public async Task Handle(AcceptRequestCommand request, CancellationToken cancellationToken)
    {
        // The "is this user an org admin" check is now AuthorizationBehavior's job
        // (PermissionKeys.OrganizationAcceptRequest), run before this handler ever executes --
        // it already re-fetches this same membership row to resolve its OrganizationId.
        var membership = await db.OrganizationMemberships.SingleOrDefaultAsync(m => m.Id == request.MembershipId, cancellationToken)
            ?? throw new NotFoundException("Join request not found.");

        if (membership.Status != MembershipStatus.Requested)
        {
            throw new ConflictException("This request is no longer pending.");
        }

        membership.Accept();
        await db.SaveChangesAsync(cancellationToken);
    }
}
