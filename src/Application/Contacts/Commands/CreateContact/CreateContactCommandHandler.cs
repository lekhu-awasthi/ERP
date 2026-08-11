using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Commands.CreateContact;

public sealed class CreateContactCommandHandler(IAppDbContext db, IDocumentNumberGenerator numberGenerator)
    : IRequestHandler<CreateContactCommand, CreateContactResult>
{
    public async Task<CreateContactResult> Handle(CreateContactCommand request, CancellationToken cancellationToken)
    {
        if (request.GroupId is { } groupId)
        {
            var groupExists = await db.ContactGroups.AnyAsync(
                x => x.Id == groupId && x.OrganizationId == request.OrganizationId, cancellationToken);

            if (!groupExists)
            {
                throw new NotFoundException("Contact group not found.");
            }
        }

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.Contact, cancellationToken);

        var contact = Contact.Create(
            request.OrganizationId,
            request.Type,
            request.Name,
            code,
            request.Address,
            request.Pan,
            request.Phone,
            request.Email,
            request.GroupId,
            request.OpeningBalance);

        db.Contacts.Add(contact);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateContactResult(contact.Id, contact.Code, contact.Type, contact.Name);
    }
}
