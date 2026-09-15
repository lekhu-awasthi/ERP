using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.BackfillDocumentLocations;

/// <summary>
/// Phase 44 (35a carried item #5, 35b #6) -- assigns the tenant's HeadOffice to every in-scope
/// document that carries no billing location.
///
/// <para><b>The gap it closes.</b> <c>LocationId</c> is nullable on all seventeen location-bearing
/// types (phase 32), and <c>LocationResolver</c> returns null for a document whose type is outside
/// the tenant's <c>LocationScopeMode</c> -- so a tenant that trades in the default
/// <i>SalesTransactionsOnly</i> mode and later widens to <i>AllTransactions</i> is left with a back
/// catalogue of purchase, inventory and accounting documents that carry no location at all. Every
/// location-filtered report then omits them while the unfiltered one still counts them, which is a
/// report disagreeing with itself.</para>
///
/// <para><b>A command, not a migration.</b> The roadmap's own wording, and the reason is that this
/// is not a schema change with one right answer: it is a tenant deciding that its historical
/// documents belong to its head office. A migration would make that decision silently, for every
/// tenant, at deploy time. This makes an Admin ask for it and reports what it did.</para>
///
/// <para><b>Idempotent by construction.</b> It only ever writes rows where the location is null, so
/// running it twice writes nothing the second time and the counts come back zero. That is what
/// makes a dry-run mode unnecessary rather than merely unbuilt.</para>
///
/// <para><b>It assigns HeadOffice and nothing cleverer.</b> Inferring a location from a document's
/// warehouse, or from the user who created it, would be a guess wearing the clothes of a fact --
/// and phase 31's rule is that a stored value nothing can justify is worse than an absent one.
/// HeadOffice is the same default <c>LocationResolver</c> already applies to every new document
/// whose caller names none, so the backfill agrees with the live write path by construction.</para>
/// </summary>
public sealed record BackfillDocumentLocationsCommand(Guid OrganizationId)
    : IRequest<BackfillDocumentLocationsResult>, IRequirePermission, IOrganizationScoped
{
    // Admin-only, at the bar phase 39's OrganizationProfileManage and phase 16a's
    // OrganizationLockDateManage set: this is a tenant-wide write over documents that are already
    // approved and already reported on. It is a decision about the business's own history, not a
    // piece of routine working data -- the discriminator CLAUDE.md states for deriving a key.
    public string PermissionKey => PermissionKeys.OrganizationBackfillLocations;
}

/// <summary>What the backfill changed, per document type. Types it found nothing to do for are
/// omitted rather than reported as zero, so the response reads as a list of what happened.</summary>
public sealed record BackfilledDocumentTypeCount(DocumentType DocumentType, int Updated);

/// <summary>
/// <paramref name="HeadOfficeId"/> is echoed so the caller can see which location was assigned
/// without a second request. <paramref name="TotalUpdated"/> is the figure the screen reports.
/// </summary>
public sealed record BackfillDocumentLocationsResult(
    Guid HeadOfficeId,
    int TotalUpdated,
    IReadOnlyList<BackfilledDocumentTypeCount> Counts);
