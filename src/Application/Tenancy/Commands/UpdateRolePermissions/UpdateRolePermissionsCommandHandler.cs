using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using FluentValidation.Results;
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
        var existingByKey = existingRows
            .Where(rp => rp.LocationId == null)
            .ToDictionary(rp => rp.PermissionKey);

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

        await ApplyLocationGrantsAsync(request, existingRows, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    /// <summary>
    /// The Location-specific half. Three rules, each with a reason:
    ///
    /// <list type="bullet">
    /// <item>only <c>LocationScopedPermissions.ScopableKeys</c> may be granted per location. A caller
    /// posting <c>Reports.*</c> or <c>Configuration.*</c> under a location is not making a narrower
    /// grant, it is making one the enforcement seam will never read -- so it is rejected rather than
    /// stored as a row nothing consults;</item>
    /// <item>the location must belong to this organization and be active. A grant at another tenant's
    /// location is the shape of a cross-tenant leak, and one at a closed branch is dead data;</item>
    /// <item>a location the caller does not mention is left alone, so a client that knows nothing of
    /// this section cannot silently revoke it.</item>
    /// </list>
    /// </summary>
    private async Task ApplyLocationGrantsAsync(
        UpdateRolePermissionsCommand request,
        List<RolePermission> existingRows,
        CancellationToken cancellationToken)
    {
        if (request.LocationGrants is not { Count: > 0 } locationGrants)
        {
            return;
        }

        var scopableKeys = LocationScopedPermissions.ScopableKeys.ToHashSet(StringComparer.Ordinal);

        var requestedLocationIds = locationGrants.Select(x => x.LocationId).Distinct().ToList();
        var validLocationIds = await db.BillingLocations
            .Where(x => x.OrganizationId == request.OrganizationId
                        && x.IsActive
                        && requestedLocationIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var missing = requestedLocationIds.Except(validLocationIds).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException("Billing location not found.");
        }

        foreach (var slice in locationGrants)
        {
            var unknown = slice.Grants.Keys.Where(key => !scopableKeys.Contains(key)).ToList();
            if (unknown.Count > 0)
            {
                throw new ValidationException(
                [
                    new ValidationFailure(
                        nameof(request.LocationGrants),
                        $"'{unknown[0]}' cannot be granted per location. Only transaction permissions are "
                        + "location-scoped; General, Settings and Reports permissions are organization-wide."),
                ]);
            }

            var existingHere = existingRows
                .Where(rp => rp.LocationId == slice.LocationId)
                .ToDictionary(rp => rp.PermissionKey);

            foreach (var key in scopableKeys)
            {
                var isGrantedNow = slice.Grants.TryGetValue(key, out var requested) && requested;

                if (existingHere.TryGetValue(key, out var row))
                {
                    if (row.IsGranted != isGrantedNow)
                    {
                        row.SetGranted(isGrantedNow);
                    }
                }
                else if (isGrantedNow)
                {
                    db.RolePermissions.Add(
                        RolePermission.Create(Guid.NewGuid(), request.RoleId, key, true, slice.LocationId));
                }
            }
        }
    }
}
