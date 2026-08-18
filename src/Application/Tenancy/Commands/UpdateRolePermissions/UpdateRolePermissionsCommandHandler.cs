using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.UpdateRolePermissions;

/// <summary>
/// Deliberately excludes the two shared system rows (OrganizationId null) from the very query
/// that looks up the target role -- their RolePermission rows are shared globally across every
/// Organization (RoleConfiguration/RolePermissionConfiguration's HasData seed), so letting one
/// tenant's Admin mutate "Member" here would silently change what every other tenant's Member can
/// do too. Only a tenant's own custom role (created via CreateRoleCommand) can have its
/// permissions edited.
/// </summary>
public sealed class UpdateRolePermissionsCommandHandler(IAppDbContext db) : IRequestHandler<UpdateRolePermissionsCommand, Unit>
{
    public async Task<Unit> Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        var role = await db.Roles.SingleOrDefaultAsync(
            r => r.Id == request.RoleId && r.OrganizationId == request.OrganizationId, cancellationToken);

        if (role is null)
        {
            var isSystemRole = await db.Roles.AnyAsync(r => r.Id == request.RoleId && r.OrganizationId == null, cancellationToken);
            throw isSystemRole
                ? new ConflictException("System roles cannot be edited.")
                : new NotFoundException("Role not found.");
        }

        var validKeys = PermissionKeyCatalog.AllKeys.ToHashSet();

        var existingRows = await db.RolePermissions.Where(rp => rp.RoleId == request.RoleId).ToListAsync(cancellationToken);
        var existingByKey = existingRows.ToDictionary(rp => rp.PermissionKey);

        foreach (var key in validKeys)
        {
            var isGrantedNow = request.Grants.TryGetValue(key, out var requested) && requested;

            if (existingByKey.TryGetValue(key, out var row))
            {
                if (row.IsGranted != isGrantedNow)
                {
                    row.SetGranted(isGrantedNow);
                }
            }
            else if (isGrantedNow)
            {
                db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), request.RoleId, key, true));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
