using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.DeleteRole;

/// <summary>
/// Rejects (409, not a silent cascade) if any OrganizationMembership still references this
/// RoleId -- the same Restrict-delete-behavior precedent this codebase already uses elsewhere
/// (e.g. Expense→Contact, WorkTask→TaskType) rather than a cascading delete that would silently
/// strand memberships pointing at a RoleId that no longer resolves.
/// </summary>
public sealed class DeleteRoleCommandHandler(IAppDbContext db) : IRequestHandler<DeleteRoleCommand, Unit>
{
    public async Task<Unit> Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Role not found.");

        if (role.OrganizationId is null)
        {
            throw new ConflictException("System roles cannot be deleted.");
        }

        if (role.OrganizationId != request.OrganizationId)
        {
            throw new NotFoundException("Role not found.");
        }

        var inUse = await db.OrganizationMemberships.AnyAsync(m => m.RoleId == request.Id, cancellationToken);
        if (inUse)
        {
            throw new ConflictException("This role is still assigned to one or more members and cannot be deleted.");
        }

        var permissionRows = await db.RolePermissions.Where(rp => rp.RoleId == request.Id).ToListAsync(cancellationToken);
        db.RolePermissions.RemoveRange(permissionRows);
        db.Roles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
