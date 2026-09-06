using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Commands.UpdateContact;

public sealed class UpdateContactCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateContactCommand, UpdateContactResult>
{
    public async Task<UpdateContactResult> Handle(UpdateContactCommand request, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Contact not found.");

        if (request.GroupId is { } groupId)
        {
            var groupExists = await db.ContactGroups.AnyAsync(
                x => x.Id == groupId && x.OrganizationId == request.OrganizationId, cancellationToken);

            if (!groupExists)
            {
                throw new NotFoundException("Contact group not found.");
            }
        }


        if (request.CreditTermId is { } creditTermId)
        {
            var creditTermExists = await db.CreditTerms.AnyAsync(
                x => x.Id == creditTermId && x.OrganizationId == request.OrganizationId, cancellationToken);

            if (!creditTermExists)
            {
                throw new NotFoundException("Credit term not found.");
            }
        }

        contact.Update(
            request.Name, request.Address, request.Pan, request.Phone, request.Email, request.GroupId,
            request.OpeningBalance, request.CreditLimit, request.CreditTermId, request.AcceptsReverseTransactions);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateContactResult(contact.Id, contact.Name);
    }
}
