using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.CheckWorkspaceNameAvailability;

public sealed class CheckWorkspaceNameAvailabilityQueryHandler(IAppDbContext db)
    : IRequestHandler<CheckWorkspaceNameAvailabilityQuery, CheckWorkspaceNameAvailabilityResult>
{
    public async Task<CheckWorkspaceNameAvailabilityResult> Handle(
        CheckWorkspaceNameAvailabilityQuery request, CancellationToken cancellationToken)
    {
        var normalized = request.WorkspaceName.Trim().ToLowerInvariant();

        var taken = await db.Organizations.AnyAsync(o => o.WorkspaceName == normalized, cancellationToken);

        return new CheckWorkspaceNameAvailabilityResult(!taken);
    }
}
