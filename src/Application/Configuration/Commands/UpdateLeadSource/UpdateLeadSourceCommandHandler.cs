using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdateLeadSource;

public sealed class UpdateLeadSourceCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateLeadSourceCommand, UpdateLeadSourceResult>
{
    public async Task<UpdateLeadSourceResult> Handle(UpdateLeadSourceCommand request, CancellationToken cancellationToken)
    {
        var leadSource = await db.LeadSources.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Lead source not found.");

        var nameTaken = await db.LeadSources.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"A lead source named '{request.Name}' already exists.");
        }

        leadSource.Update(request.Name, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateLeadSourceResult(leadSource.Id, leadSource.Name, leadSource.IsActive);
    }
}
