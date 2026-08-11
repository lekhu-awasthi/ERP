using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Commands.UpdateContactGroup;

public sealed class UpdateContactGroupCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateContactGroupCommand, UpdateContactGroupResult>
{
    public async Task<UpdateContactGroupResult> Handle(UpdateContactGroupCommand request, CancellationToken cancellationToken)
    {
        var contactGroup = await db.ContactGroups.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Contact group not found.");

        if (request.ParentGroupId == request.Id)
        {
            throw new ConflictException("A contact group cannot be its own parent.");
        }

        var nameTaken = await db.ContactGroups.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
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

        contactGroup.Update(request.Name, request.ParentGroupId, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateContactGroupResult(
            contactGroup.Id, contactGroup.Name, contactGroup.ParentGroupId, contactGroup.IsActive);
    }
}
