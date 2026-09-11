using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Accounting.Reports;

/// <summary>
/// Phase 35b -- the Billing Location narrowing every GL report applies, stated once.
///
/// <para>Nine reports read <see cref="GlJournalEntry"/> and all nine carry a Billing Location filter
/// live (confirm-live 2026-09-10, a census of all 49 report filter bars): Trial Balance, Balance
/// Sheet, Income Statement, the Journal report, General Ledger Summary, Detail General Ledger, GL
/// Master, Cash Flow Summary and Net Trading Assets. Each of them builds a different aggregation on
/// top of the same two-line narrowing, which is the whole of what they share.</para>
///
/// <para><b>Why an <c>IQueryable&lt;GlJournalEntry&gt;</c> and not a predicate.</b> A shared matcher
/// invoked from inside a <c>Where</c> lambda is a static call the LINQ provider cannot translate,
/// and InMemory evaluates it in C# so every handler test passes while the endpoint 500s
/// (phase-34b's gotcha, phase-25's captured-<c>Func</c> through another door). Composing
/// <c>.Where()</c> onto a concrete, non-generic queryable has neither problem: the expression trees
/// are built here against <see cref="GlJournalEntry"/> itself and the caller joins onto the result.
/// It is also why the two conditions are two separate <c>Where</c> calls rather than one
/// <c>requested == null || ...</c> clause -- the codebase's stated shape, and the one immune to both
/// mechanisms phase 35a measured.</para>
///
/// <para><b>The two arguments mean different things and both must be applied.</b>
/// <paramref name="requested"/> is the user's own filter, null meaning "All locations", which is the
/// live default. <paramref name="scope"/> is <c>LocationAccessScope.ForReportsAsync</c>'s answer --
/// the locations this caller is *permitted* to see rows from under
/// <c>TenantSettings.LocationWiseReportPermission</c> -- where null means unrestricted and an empty
/// list would mean the opposite. A caller asking for a location outside their scope gets no rows,
/// which is the correct answer and needs no special case: the two <c>Where</c>s simply both
/// apply.</para>
///
/// <para><b>A null <c>LocationId</c> is excluded by either filter</b>, deliberately. An entry with
/// no location is one whose document was raised while its type was outside the tenant's
/// <c>LocationScopeMode</c>, or one older than phase 35b's backfill could reach. It belongs to no
/// branch, so a report asked for one branch must not show it -- and <c>x.LocationId == id</c>
/// already does that, since a null never equals a value.</para>
/// </summary>
internal static class GlEntryLocations
{
    /// <summary>
    /// The tenant's posted GL entries, narrowed to a requested location and to the caller's report
    /// scope. Callers join <c>GlLines</c> onto the result rather than filtering lines directly --
    /// the location lives on the entry, not the line.
    ///
    /// <para><b>The tenant filter is applied here, not by the caller.</b> Every one of these nine
    /// handlers previously wrote <c>entry.OrganizationId == request.OrganizationId</c> inside the
    /// join's own <c>where</c>, and nine handlers rewritten to join onto a pre-filtered queryable is
    /// nine chances to drop it -- with no global query filter in this codebase to catch it (CLAUDE.md:
    /// every handler filters by OrganizationId in LINQ, by hand). Taking the organization as an
    /// argument makes that impossible rather than merely unlikely.</para>
    /// </summary>
    internal static IQueryable<GlJournalEntry> ForReport(
        IAppDbContext db, Guid organizationId, Guid? requested, IReadOnlyList<Guid>? scope)
    {
        var entries = db.GlJournalEntries.Where(x => x.OrganizationId == organizationId);

        if (requested is { } locationId)
        {
            entries = entries.Where(x => x.LocationId == locationId);
        }

        if (scope is not null)
        {
            entries = entries.Where(x => x.LocationId != null && scope.Contains(x.LocationId.Value));
        }

        return entries;
    }
}
