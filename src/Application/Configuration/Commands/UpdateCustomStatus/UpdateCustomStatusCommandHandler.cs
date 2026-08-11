using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdateCustomStatus;

public sealed class UpdateCustomStatusCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateCustomStatusCommand, UpdateCustomStatusResult>
{
    public async Task<UpdateCustomStatusResult> Handle(UpdateCustomStatusCommand request, CancellationToken cancellationToken)
    {
        var customStatus = await db.CustomStatuses.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Custom status not found.");

        var nameTaken = await db.CustomStatuses.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.Id != request.Id
                 && x.DocumentType == request.DocumentType
                 && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException(
                $"A custom status named '{request.Name}' already exists for {request.DocumentType}.");
        }

        customStatus.Update(request.Name, request.DocumentType, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateCustomStatusResult(customStatus.Id, customStatus.Name, customStatus.DocumentType, customStatus.IsActive);
    }
}
