using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.DeleteCustomFieldDefinition;

public sealed class DeleteCustomFieldDefinitionCommandHandler(IAppDbContext db)
    : IRequestHandler<DeleteCustomFieldDefinitionCommand, Unit>
{
    public async Task<Unit> Handle(DeleteCustomFieldDefinitionCommand request, CancellationToken cancellationToken)
    {
        var definition = await db.CustomFieldDefinitions.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Custom field not found.");

        db.CustomFieldDefinitions.Remove(definition);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
