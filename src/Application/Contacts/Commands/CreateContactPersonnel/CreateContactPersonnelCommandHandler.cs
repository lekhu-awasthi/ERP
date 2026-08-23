using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Application.Contacts.Commands.CreateContactPersonnel;

public sealed class CreateContactPersonnelCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateContactPersonnelCommand, ContactPersonnelResult>
{
    public async Task<ContactPersonnelResult> Handle(CreateContactPersonnelCommand request, CancellationToken cancellationToken)
    {
        await ContactsValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);

        var personnel = ContactPersonnel.Create(
            request.OrganizationId, request.ContactId, request.Name, request.Address, request.Code,
            request.Phone, request.GroupId, request.Email, request.OrganizationTitle);

        db.ContactPersonnel.Add(personnel);
        await db.SaveChangesAsync(cancellationToken);

        return new ContactPersonnelResult(
            personnel.Id, personnel.ContactId, personnel.Name, personnel.Address, personnel.Code,
            personnel.Phone, personnel.GroupId, personnel.Email, personnel.OrganizationTitle);
    }
}
