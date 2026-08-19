using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.GetOrganizationLockDate;

public sealed class GetOrganizationLockDateQueryHandler(IAppDbContext db)
    : IRequestHandler<GetOrganizationLockDateQuery, GetOrganizationLockDateResult>
{
    public async Task<GetOrganizationLockDateResult> Handle(
        GetOrganizationLockDateQuery request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.SingleOrDefaultAsync(
            x => x.Id == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        return new GetOrganizationLockDateResult(organization.Id, organization.LockDate);
    }
}
