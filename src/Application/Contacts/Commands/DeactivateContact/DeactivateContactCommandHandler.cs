using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Commands.DeactivateContact;

public sealed class DeactivateContactCommandHandler(IAppDbContext db)
    : IRequestHandler<DeactivateContactCommand, Unit>
{
    public async Task<Unit> Handle(DeactivateContactCommand request, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Contact not found.");

        contact.Deactivate();
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
