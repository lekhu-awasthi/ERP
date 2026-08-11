using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.GetContact;

public sealed class GetContactQueryHandler(IAppDbContext db) : IRequestHandler<GetContactQuery, Contact>
{
    public async Task<Contact> Handle(GetContactQuery request, CancellationToken cancellationToken)
    {
        return await db.Contacts.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Contact not found.");
    }
}
