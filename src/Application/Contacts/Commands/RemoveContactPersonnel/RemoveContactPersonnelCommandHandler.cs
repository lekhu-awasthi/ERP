using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Commands.RemoveContactPersonnel;

public sealed class RemoveContactPersonnelCommandHandler(IAppDbContext db) : IRequestHandler<RemoveContactPersonnelCommand, Unit>
{
    public async Task<Unit> Handle(RemoveContactPersonnelCommand request, CancellationToken cancellationToken)
    {
        var personnel = await db.ContactPersonnel.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.ContactId == request.ContactId && x.OrganizationId == request.OrganizationId,
            cancellationToken)
            ?? throw new NotFoundException("Contact personnel not found.");

        db.ContactPersonnel.Remove(personnel);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
