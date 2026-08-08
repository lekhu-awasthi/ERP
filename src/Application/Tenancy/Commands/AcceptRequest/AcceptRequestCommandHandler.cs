using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.AcceptRequest;

public sealed class AcceptRequestCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<AcceptRequestCommand>
{
    public async Task Handle(AcceptRequestCommand request, CancellationToken cancellationToken)
    {
        var membership = await db.OrganizationMemberships.SingleOrDefaultAsync(m => m.Id == request.MembershipId, cancellationToken)
            ?? throw new NotFoundException("Join request not found.");

        if (membership.Status != MembershipStatus.Requested)
        {
            throw new ConflictException("This request is no longer pending.");
        }

        var isAdmin = await db.OrganizationMemberships.AnyAsync(
            m => m.OrganizationId == membership.OrganizationId
                 && m.UserId == currentUser.UserId
                 && m.Status == MembershipStatus.Accepted
                 && m.Role == MembershipRole.Admin,
            cancellationToken);

        if (!isAdmin)
        {
            throw new ForbiddenException("Only an organization admin can accept join requests.");
        }

        membership.Accept();
        await db.SaveChangesAsync(cancellationToken);
    }
}
