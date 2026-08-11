using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.UpdateAccountGroup;

public sealed class UpdateAccountGroupCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateAccountGroupCommand, UpdateAccountGroupResult>
{
    public async Task<UpdateAccountGroupResult> Handle(UpdateAccountGroupCommand request, CancellationToken cancellationToken)
    {
        var accountGroup = await db.AccountGroups.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Account group not found.");

        if (request.ParentGroupId == request.Id)
        {
            throw new ConflictException("An account group cannot be its own parent.");
        }

        var nameTaken = await db.AccountGroups.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"An account group named '{request.Name}' already exists.");
        }

        if (request.ParentGroupId is { } parentGroupId)
        {
            var parent = await db.AccountGroups.SingleOrDefaultAsync(
                x => x.Id == parentGroupId && x.OrganizationId == request.OrganizationId, cancellationToken)
                ?? throw new NotFoundException("Parent account group not found.");

            if (parent.RootType != accountGroup.RootType)
            {
                throw new ConflictException("An account group must have the same root type as its parent.");
            }
        }

        accountGroup.Update(request.Name, request.ParentGroupId, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateAccountGroupResult(
            accountGroup.Id, accountGroup.Name, accountGroup.RootType, accountGroup.ParentGroupId, accountGroup.IsActive);
    }
}
