using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.CreateAccountGroup;

public sealed class CreateAccountGroupCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateAccountGroupCommand, CreateAccountGroupResult>
{
    public async Task<CreateAccountGroupResult> Handle(CreateAccountGroupCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.AccountGroups.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"An account group named '{request.Name}' already exists.");
        }

        if (request.ParentGroupId is { } parentGroupId)
        {
            var parent = await db.AccountGroups.SingleOrDefaultAsync(
                x => x.Id == parentGroupId && x.OrganizationId == request.OrganizationId, cancellationToken)
                ?? throw new NotFoundException("Parent account group not found.");

            if (parent.RootType != request.RootType)
            {
                throw new ConflictException("An account group must have the same root type as its parent.");
            }
        }

        var accountGroup = AccountGroup.Create(request.OrganizationId, request.Name, request.RootType, request.ParentGroupId);
        db.AccountGroups.Add(accountGroup);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateAccountGroupResult(accountGroup.Id, accountGroup.Name, accountGroup.RootType, accountGroup.ParentGroupId);
    }
}
