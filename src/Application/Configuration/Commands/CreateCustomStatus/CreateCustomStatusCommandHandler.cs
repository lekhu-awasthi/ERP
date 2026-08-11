using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreateCustomStatus;

public sealed class CreateCustomStatusCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateCustomStatusCommand, CreateCustomStatusResult>
{
    public async Task<CreateCustomStatusResult> Handle(CreateCustomStatusCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.CustomStatuses.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.DocumentType == request.DocumentType
                 && x.Name == request.Name,
            cancellationToken);

        if (nameExists)
        {
            throw new ConflictException(
                $"A custom status named '{request.Name}' already exists for {request.DocumentType}.");
        }

        var customStatus = CustomStatus.Create(request.OrganizationId, request.Name, request.DocumentType);
        db.CustomStatuses.Add(customStatus);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateCustomStatusResult(customStatus.Id, customStatus.Name, customStatus.DocumentType);
    }
}
