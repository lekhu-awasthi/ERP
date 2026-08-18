using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.CreateRole;

public sealed class CreateRoleCommandHandler(IAppDbContext db) : IRequestHandler<CreateRoleCommand, CreateRoleResult>
{
    public async Task<CreateRoleResult> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        // Includes the two shared system rows (OrganizationId null) in the name-collision check --
        // a custom role named "Admin" would be confusing in the role picker even though it wouldn't
        // technically violate the (OrganizationId, Name) unique index (system rows have a null
        // OrganizationId, distinct from this org's).
        var nameExists = await db.Roles.AnyAsync(
            r => (r.OrganizationId == null || r.OrganizationId == request.OrganizationId) && r.Name == request.Name,
            cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A role named '{request.Name}' already exists.");
        }

        var role = Role.CreateCustom(request.OrganizationId, request.Name, request.Description);
        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateRoleResult(role.Id, role.Name, role.Description);
    }
}
