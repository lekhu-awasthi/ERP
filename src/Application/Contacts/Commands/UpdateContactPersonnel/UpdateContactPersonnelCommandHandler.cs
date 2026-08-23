using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContactPersonnel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Commands.UpdateContactPersonnel;

public sealed class UpdateContactPersonnelCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateContactPersonnelCommand, ContactPersonnelResult>
{
    public async Task<ContactPersonnelResult> Handle(UpdateContactPersonnelCommand request, CancellationToken cancellationToken)
    {
        var personnel = await db.ContactPersonnel.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.ContactId == request.ContactId && x.OrganizationId == request.OrganizationId,
            cancellationToken)
            ?? throw new NotFoundException("Contact personnel not found.");

        personnel.Update(
            request.Name, request.Address, request.Code, request.Phone, request.GroupId, request.Email,
            request.OrganizationTitle);

        await db.SaveChangesAsync(cancellationToken);

        return new ContactPersonnelResult(
            personnel.Id, personnel.ContactId, personnel.Name, personnel.Address, personnel.Code,
            personnel.Phone, personnel.GroupId, personnel.Email, personnel.OrganizationTitle);
    }
}
