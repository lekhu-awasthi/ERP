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

        if (contact.IsWalkInCustomer)
        {
            // Phase 60 -- a 409 naming the reason, not the Domain backstop's 500 (phase 39).
            throw new ConflictException(
                $"'{contact.Name}' is this organization's walk-in customer, which the point of sale raises "
                + "every anonymous sale against, and cannot be deactivated.");
        }

        contact.Deactivate();
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
