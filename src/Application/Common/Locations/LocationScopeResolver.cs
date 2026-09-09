using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Locations;

/// <summary>
/// What <see cref="LocationScopeResolver"/> found. The three cases are distinct because the caller
/// answers each one differently.
/// </summary>
public enum LocationScopeOutcome
{
    /// <summary>The request has no location dimension: this document type is out of scope for the
    /// tenant's <c>LocationScopeMode</c>, or the row exists but carries no location because it was
    /// written while the type was out of scope. There is no location-scoped route to the action, so
    /// the organization-wide refusal stands (403).</summary>
    NoLocation,

    /// <summary>The targeted row does not exist. The pipeline must let the request through so the
    /// handler gives its own 404 -- that leg is what proves a refused caller holds the pipeline key
    /// (phase-31's both-directions rule), and a nonexistent id discloses nothing.</summary>
    TargetMissing,

    /// <summary>One or two real locations the caller must hold the key at.</summary>
    Resolved,
}

/// <summary>
/// Phase 32b -- <b>every billing location a single request touches</b>, which is what a
/// location-scoped caller must hold the key at. Read by <c>AuthorizationBehavior</c>, and only ever
/// on the slow path (a caller who holds the key organization-wide never reaches here).
///
/// <para>There are up to two, and an update carries both:</para>
/// <list type="bullet">
/// <item>the location the request <i>writes</i> -- <see cref="ILocationBearingCommand.LocationId"/>,
/// or the tenant's HeadOffice when the caller left it blank, which is what
/// <see cref="LocationResolver"/> will store;</item>
/// <item>the location the targeted row <i>already has</i> --
/// <see cref="ILocationScopedDocument"/>, read by <see cref="DocumentLocationReader"/>.</item>
/// </list>
///
/// <para><b>Both, not either.</b> A Member scoped to HeadOffice must be refused an edit of a POS
/// Retail invoice (the row's location) <i>and</i> refused moving a HeadOffice invoice to POS Retail
/// (the written location) -- and an Update command is the one request that can do both at once. So
/// the caller must hold the key at every location returned, not at any of them.</para>
/// </summary>
public static class LocationScopeResolver
{
    public static async Task<(LocationScopeOutcome Outcome, IReadOnlyList<Guid> LocationIds)> CandidateLocationsAsync(
        IAppDbContext db,
        Guid organizationId,
        DocumentType documentType,
        object request,
        CancellationToken cancellationToken)
    {
        var mode = await db.TenantSettings
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => (LocationScopeMode?)x.LocationScopeMode)
            .SingleOrDefaultAsync(cancellationToken) ?? LocationScopeMode.SalesTransactionsOnly;

        if (!DocumentLocationScope.AppliesTo(documentType, mode))
        {
            return (LocationScopeOutcome.NoLocation, []);
        }

        var candidates = new List<Guid>(2);

        if (TargetIdOf(request) is { } targetId)
        {
            var targetType = (request as ILocationScopedDocument)?.LocationDocumentTypeOverride ?? documentType;

            var (found, stored) = await DocumentLocationReader.ReadAsync(
                db, targetType, targetId, cancellationToken);

            if (!found)
            {
                return (LocationScopeOutcome.TargetMissing, []);
            }

            if (stored is { } storedLocation)
            {
                candidates.Add(storedLocation);
            }
        }

        if (request is ILocationBearingCommand write)
        {
            // A supplied id is taken at face value rather than validated here: LocationResolver
            // validates it inside the handler and 404s a bogus one, and duplicating that check would
            // only move the same error earlier. A bogus id therefore 403s instead of 404ing for a
            // location-scoped caller, which leaks nothing -- a location id is not a secret, and the
            // caller could not have written the document either way.
            var written = write.LocationId ?? await DefaultLocationIdAsync(db, organizationId, cancellationToken);

            if (written is { } writtenLocation && !candidates.Contains(writtenLocation))
            {
                candidates.Add(writtenLocation);
            }
        }

        return candidates.Count == 0
            ? (LocationScopeOutcome.NoLocation, [])
            : (LocationScopeOutcome.Resolved, candidates);
    }

    /// <summary>
    /// Which existing document row, if any, this request targets. The document's <i>type</i> comes
    /// from the permission key rather than from the request, so there is only ever one statement of
    /// it -- see <see cref="ILocationScopedDocument"/>.
    ///
    /// <para><see cref="ILocationScopedDocument"/> is the explicit answer. Falling back to
    /// <see cref="Security.ILockDateSensitiveDocument"/> is what spares all thirty Approve and Void
    /// commands an edit they would otherwise need: those already tell the pipeline exactly which
    /// document they act on, in exactly this shape, and re-declaring it under a second name would be
    /// two properties that must agree forever. This is <i>reading</i> an existing marker, not merging
    /// the two -- the sets still differ (a lock date never gates a read), and
    /// <c>LocationScopeSweepGuardTests</c> pins that every lock-date-sensitive document request
    /// resolves a target here, so an Approve command that ever drops that interface fails the build
    /// rather than silently losing its location check.</para>
    /// </summary>
    private static Guid? TargetIdOf(object request) => request switch
    {
        ILocationScopedDocument explicitTarget => explicitTarget.LocationDocumentId,
        Security.ILockDateSensitiveDocument approval => approval.LockDateDocumentId,
        _ => null,
    };

    /// <summary>The tenant's HeadOffice row -- what <see cref="LocationResolver"/> stores when the
    /// caller supplies nothing, so the permission check has to test the same value the write will.</summary>
    private static Task<Guid?> DefaultLocationIdAsync(
        IAppDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
        db.BillingLocations
            .Where(x => x.OrganizationId == organizationId
                        && x.LocationType == BillingLocationType.HeadOffice
                        && x.IsActive)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
}
