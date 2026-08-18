using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreateLeadSource;

public sealed class CreateLeadSourceCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateLeadSourceCommand, CreateLeadSourceResult>
{
    public async Task<CreateLeadSourceResult> Handle(CreateLeadSourceCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.LeadSources.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A lead source named '{request.Name}' already exists.");
        }

        var leadSource = LeadSource.Create(request.OrganizationId, request.Name);
        db.LeadSources.Add(leadSource);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateLeadSourceResult(leadSource.Id, leadSource.Name);
    }
}
