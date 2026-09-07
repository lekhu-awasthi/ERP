using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Locations;

/// <summary>
/// Turns a command's optional <c>LocationId</c> into the value a document should actually store
/// (Phase 32). One place, called from all 17 Create/Update handlers, so the four rules below hold
/// identically everywhere instead of being re-derived per module:
///
/// <list type="number">
/// <item>if the tenant's <see cref="LocationScopeMode"/> puts this document type out of scope, the
/// stored location is <b>null</b> -- even if the caller supplied one. A client that keeps sending a
/// location after an Admin narrows the scope must not quietly keep writing it, or narrowing the
/// setting would be cosmetic;</item>
/// <item>a supplied location must exist, belong to this organization and be <b>active</b>. An
/// inactive location is a branch the tenant has closed; new documents must not be raised from it,
/// though historical ones keep pointing at it (which is why deactivation is allowed at all and
/// deletion is not);</item>
/// <item>a null from a caller means "use the default", which is the tenant's HeadOffice row -- the
/// live document header defaults to <c>HeadOffice (HO)</c> rather than to blank;</item>
/// <item>if the tenant somehow has no active HeadOffice, the result is null rather than an error.
/// Seeding makes that unreachable for any organization created or migrated by this phase, and
/// failing a document save over a missing default would be the phase-20f failure mode -- a tenant
/// unable to invoice at all because of a feature it never asked for.</item>
/// </list>
/// </summary>
public static class LocationResolver
{
    public static async Task<Guid?> ResolveAsync(
        IAppDbContext db,
        Guid organizationId,
        DocumentType documentType,
        Guid? requestedLocationId,
        CancellationToken cancellationToken)
    {
        var mode = await db.TenantSettings
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => (LocationScopeMode?)x.LocationScopeMode)
            .SingleOrDefaultAsync(cancellationToken) ?? LocationScopeMode.SalesTransactionsOnly;

        if (!DocumentLocationScope.AppliesTo(documentType, mode))
        {
            return null;
        }

        if (requestedLocationId is { } requested)
        {
            var exists = await db.BillingLocations.AnyAsync(
                x => x.Id == requested && x.OrganizationId == organizationId && x.IsActive,
                cancellationToken);

            if (!exists)
            {
                throw new NotFoundException("Billing location not found.");
            }

            return requested;
        }

        return await db.BillingLocations
            .Where(x => x.OrganizationId == organizationId
                        && x.LocationType == BillingLocationType.HeadOffice
                        && x.IsActive)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
