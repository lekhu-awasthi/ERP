using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.SetOrganizationLockDate;

public sealed class SetOrganizationLockDateCommandHandler(IAppDbContext db)
    : IRequestHandler<SetOrganizationLockDateCommand, SetOrganizationLockDateResult>
{
    public async Task<SetOrganizationLockDateResult> Handle(
        SetOrganizationLockDateCommand request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.SingleOrDefaultAsync(
            x => x.Id == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        organization.SetLockDate(request.LockDate);

        await db.SaveChangesAsync(cancellationToken);

        return new SetOrganizationLockDateResult(organization.Id, organization.LockDate);
    }
}
