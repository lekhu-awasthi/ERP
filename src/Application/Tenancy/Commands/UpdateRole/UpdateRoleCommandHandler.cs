using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.UpdateRole;

public sealed class UpdateRoleCommandHandler(IAppDbContext db) : IRequestHandler<UpdateRoleCommand, UpdateRoleResult>
{
    public async Task<UpdateRoleResult> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Role not found.");

        // The two shared system rows (OrganizationId null) are deliberately not editable through
        // this command -- see Role's doc comment for why (their RolePermission rows are shared
        // globally, not per-org).
        if (role.OrganizationId is null)
        {
            throw new ConflictException("System roles cannot be edited.");
        }

        if (role.OrganizationId != request.OrganizationId)
        {
            // Belongs to a different tenant -- treated as not found rather than forbidden, so this
            // endpoint doesn't leak whether a given RoleId exists at all in another Organization.
            throw new NotFoundException("Role not found.");
        }

        var nameTaken = await db.Roles.AnyAsync(
            r => r.Id != request.Id
                 && (r.OrganizationId == null || r.OrganizationId == request.OrganizationId)
                 && r.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"A role named '{request.Name}' already exists.");
        }

        role.Update(request.Name, request.Description);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateRoleResult(role.Id, role.Name, role.Description);
    }
}
