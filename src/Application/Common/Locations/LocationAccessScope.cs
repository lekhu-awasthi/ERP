using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Locations;

/// <summary>
/// Phase 32b -- <b>which billing locations may this caller see rows from?</b> The read-side twin of
/// the write-side check in <c>AuthorizationBehavior</c>: where that one refuses an action on a
/// document outside the caller's locations, this one narrows a list or a report to them.
///
/// <para><c>null</c> means <b>unrestricted</b>, and it is the answer for almost everyone: a caller
/// holding the key organization-wide sees every location ("Apply across all billing locations", the
/// live editor's own subtitle for that section), and so does any caller on a tenant that has never
/// granted a location-scoped row. An empty set would mean the opposite -- see nothing -- so the two
/// must not be conflated, which is why this returns a nullable list rather than a set that happens
/// to be empty.</para>
///
/// <para><b>Why the report scope reads transaction grants.</b>
/// <c>TenantSettings.LocationWiseReportPermission</c> was recorded by phase 32, by the roadmap and
/// by the module scan as the switch that "pulls the 52 Reports keys into location scope". The
/// 2026-09-09 confirm-live pass turned it on, reloaded, and found the role editor's
/// Location-specific section unchanged at 94 x N with no Reports group anywhere in it -- so that
/// reading is wrong. What the toggle's own label says is the whole of it: <i>"Restrict users to view
/// reports only for locations they have access to."</i> A user's locations are the ones their role
/// carries location-specific grants at, and turning the toggle on applies that same set to report
/// rows. There is no separate per-location report grant to store, which is also why this phase adds
/// no report permission keys.</para>
/// </summary>
public static class LocationAccessScope
{
    /// <summary>
    /// The locations a caller may see rows from for one location-scopable key, or null for
    /// unrestricted. Used by every <see cref="ILocationFilteredQuery"/> list handler.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>?> ForKeyAsync(
        IAppDbContext db,
        ICurrentUserService currentUser,
        Guid organizationId,
        string permissionKey,
        CancellationToken cancellationToken)
    {
        if (!LocationScopedPermissions.IsLocationScopable(permissionKey))
        {
            return null;
        }

        var organizationWide = await GrantedPermissionReader.GrantedKeysAsync(
            db, organizationId, currentUser.UserId, cancellationToken);

        if (organizationWide.Contains(permissionKey))
        {
            return null;
        }

        var locations = await GrantedPermissionReader.GrantedLocationsAsync(
            db, organizationId, currentUser.UserId, permissionKey, cancellationToken);

        // Nothing at all: the caller cannot have reached a handler without either grant, because
        // AuthorizationBehavior would have refused them. Treat it as unrestricted rather than
        // inventing an empty result, so a future caller that is not permission-gated at all does not
        // silently see zero rows.
        return locations.Count == 0 ? null : [.. locations];
    }

    /// <summary>
    /// The locations a caller may see report rows from, or null for unrestricted. Returns null
    /// whenever <c>TenantSettings.LocationWiseReportPermission</c> is off, which is the default and
    /// what every tenant before this phase has.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>?> ForReportsAsync(
        IAppDbContext db,
        ICurrentUserService currentUser,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var restrict = await db.TenantSettings
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => (bool?)x.LocationWiseReportPermission)
            .SingleOrDefaultAsync(cancellationToken) ?? false;

        if (!restrict)
        {
            return null;
        }

        var locations = await GrantedPermissionReader.AnyGrantedLocationsAsync(
            db, organizationId, currentUser.UserId, cancellationToken);

        // A role with no location-specific grant at all is an organization-wide role, and the toggle
        // is not meant to blind it -- it restricts users who *have* locations to those locations.
        return locations.Count == 0 ? null : [.. locations];
    }
}
