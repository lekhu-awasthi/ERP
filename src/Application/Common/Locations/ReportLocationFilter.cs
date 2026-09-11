using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Locations;

/// <summary>
/// Phase 35b -- the Billing Location narrowing every <b>document-sourced</b> report applies, stated
/// once. <c>GlEntryLocations</c> is its counterpart for the nine reports that read the GL instead.
///
/// <para><b>Why generic over <c>EF.Property</c> rather than one <c>Where</c> per type.</b> The
/// seventeen location-bearing aggregates share no interface, and a generic method's
/// <c>x =&gt; x.LocationId</c> would bake in the *interface's* MemberInfo, which EF cannot match back
/// to the concrete entity's mapped property (phase-2 bugs #1/#5). <see cref="EF.Property{T}"/>
/// resolves by name at translation time and is the documented remedy. It is not a new bet: phase 33
/// shipped exactly this expression over exactly this column for all fifteen document aggregates in
/// <c>GlobalSearchQueryHandler.ByCodeAsync</c>, and it runs against SQL Server today.</para>
///
/// <para><b>Composed <c>Where</c> calls, never a folded predicate.</b> An expression tree does not
/// short-circuit, so <c>scope == null || scope.Contains(...)</c> would hand EF a null list to
/// translate on the unrestricted branch -- the shape <c>known-gotchas.md</c> forbids. (Phase 35a
/// measured that the collection-valued form happens to survive funcletization on SQL Server; the
/// rewrite stayed anyway, because relying on which of two mechanisms applies is not a design.)</para>
///
/// <para><b>A row with no location is hidden from either narrowing</b>, deliberately and for two
/// different reasons. Under an explicit filter, a document that belongs to no branch is not a
/// document of the branch asked for. Under the permission scope, it matches
/// <c>AuthorizationBehavior</c>'s own <c>LocationScopeOutcome.NoLocation</c> branch: there is
/// nothing a location grant can cover.</para>
/// </summary>
internal static class ReportLocationFilter
{
    /// <summary>
    /// The column name read by string. Declared with <c>nameof</c> against a real aggregate so it is
    /// at least one real type's real member, and pinned across all seventeen by
    /// <c>ReportLocationSweepGuardTests</c> -- a rename then breaks the build rather than surfacing
    /// as a runtime translation failure on 36 endpoints.
    /// </summary>
    internal const string LocationIdProperty = nameof(Invoice.LocationId);

    /// <summary>
    /// Narrows a document query to a requested billing location and to the caller's report scope.
    /// Both arguments are independent: <paramref name="requested"/> is the user's own filter (null =
    /// "All locations") and <paramref name="scope"/> is
    /// <c>LocationAccessScope.ForReportsAsync</c>'s answer (null = unrestricted). A caller asking for
    /// a location outside their scope correctly gets nothing.
    /// </summary>
    internal static IQueryable<T> AtLocations<T>(
        this IQueryable<T> query, Guid? requested, IReadOnlyList<Guid>? scope)
        where T : class
    {
        if (requested is { } locationId)
        {
            query = query.Where(x => EF.Property<Guid?>(x, LocationIdProperty) == locationId);
        }

        if (scope is not null)
        {
            var ids = scope.ToList();

            query = query.Where(x =>
                EF.Property<Guid?>(x, LocationIdProperty) != null
                && ids.Contains(EF.Property<Guid?>(x, LocationIdProperty)!.Value));
        }

        return query;
    }
}
