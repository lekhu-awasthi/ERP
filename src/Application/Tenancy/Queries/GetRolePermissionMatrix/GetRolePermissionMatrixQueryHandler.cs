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

        var rows = await db.RolePermissions
            .Where(rp => rp.RoleId == request.RoleId && rp.IsGranted)
            .Select(rp => new { rp.PermissionKey, rp.LocationId })
            .ToListAsync(cancellationToken);

        var organizationWide = rows.Where(r => r.LocationId == null).Select(r => r.PermissionKey).ToHashSet(StringComparer.Ordinal);
        var perLocation = rows
            .Where(r => r.LocationId != null)
            .GroupBy(r => r.LocationId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(r => r.PermissionKey).ToHashSet(StringComparer.Ordinal));

        var groups = Group(PermissionKeyCatalog.AllKeys, organizationWide);

        // Active locations only, and only when there is more than one: a tenant capped at a single
        // HeadOffice location has nothing to scope, so the whole second section is absent rather than
        // rendering one collapsible that can only ever duplicate the organization-wide answer
        // (phase 32b Decision D -- a one-location tenant pays nothing, in the UI as well as the
        // pipeline).
        var locations = await db.BillingLocations
            .Where(x => x.OrganizationId == request.OrganizationId && x.IsActive)
            .OrderBy(x => x.LocationType)
            .ThenBy(x => x.Code)
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToListAsync(cancellationToken);

        var sections = locations.Count <= 1
            ? []
            : locations.Select(location => new LocationPermissionSectionDto(
                    location.Id,
                    location.Code,
                    location.Name,
                    Group(
                        LocationScopedPermissions.ScopableKeys,
                        perLocation.GetValueOrDefault(location.Id) ?? [])))
                .ToList();

        return new RolePermissionMatrixDto(role.Id, role.Name, role.OrganizationId == null, groups, sections);
    }

    private static List<PermissionMatrixGroupDto> Group(IEnumerable<string> keys, IReadOnlySet<string> granted) =>
        keys.GroupBy(PermissionKeyCatalog.ModuleOf)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PermissionMatrixGroupDto(
                g.Key,
                g.Select(key => new PermissionMatrixEntryDto(key, granted.Contains(key))).ToList()))
            .ToList();
}
