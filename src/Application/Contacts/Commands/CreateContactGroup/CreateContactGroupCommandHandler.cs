using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Commands.CreateContactGroup;

public sealed class CreateContactGroupCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateContactGroupCommand, CreateContactGroupResult>
{
    public async Task<CreateContactGroupResult> Handle(CreateContactGroupCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.ContactGroups.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A contact group named '{request.Name}' already exists.");
        }

        if (request.ParentGroupId is { } parentGroupId)
        {
            var parentExists = await db.ContactGroups.AnyAsync(
                x => x.Id == parentGroupId && x.OrganizationId == request.OrganizationId, cancellationToken);

            if (!parentExists)
            {
                throw new NotFoundException("Parent contact group not found.");
            }
        }

        var contactGroup = ContactGroup.Create(request.OrganizationId, request.Name, request.ParentGroupId);
        db.ContactGroups.Add(contactGroup);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateContactGroupResult(contactGroup.Id, contactGroup.Name, contactGroup.ParentGroupId);
    }
}
