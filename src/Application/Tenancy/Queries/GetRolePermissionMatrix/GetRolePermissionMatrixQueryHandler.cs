using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.GetRolePermissionMatrix;

public sealed class GetRolePermissionMatrixQueryHandler(IAppDbContext db)
    : IRequestHandler<GetRolePermissionMatrixQuery, RolePermissionMatrixDto>
{
    public async Task<RolePermissionMatrixDto> Handle(GetRolePermissionMatrixQuery request, CancellationToken cancellationToken)
    {
        var role = await db.Roles.SingleOrDefaultAsync(
            r => r.Id == request.RoleId && (r.OrganizationId == null || r.OrganizationId == request.OrganizationId),
            cancellationToken)
            ?? throw new NotFoundException("Role not found.");

        var grantedKeys = await db.RolePermissions
            .Where(rp => rp.RoleId == request.RoleId && rp.IsGranted)
            .Select(rp => rp.PermissionKey)
            .ToListAsync(cancellationToken);
        var grantedSet = grantedKeys.ToHashSet();

        var groups = PermissionKeyCatalog.AllKeys
            .GroupBy(PermissionKeyCatalog.ModuleOf)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PermissionMatrixGroupDto(
                g.Key,
                g.Select(key => new PermissionMatrixEntryDto(key, grantedSet.Contains(key))).ToList()))
            .ToList();

        return new RolePermissionMatrixDto(role.Id, role.Name, role.OrganizationId == null, groups);
    }
}
