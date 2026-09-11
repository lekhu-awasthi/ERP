namespace ErpApp.Application.Common.Locations;

/// <summary>
/// Phase 35b -- a report query that takes the <b>Billing Location</b> filter.
///
/// <para><b>The set is a census, not a guess.</b> The 2026-09-10 confirm-live pass opened all 49
/// report screens in the reference product's catalogue and read every filter bar: <b>43 carry a
/// "Billing Location (All)" control and 6 do not</b>. The six are the three IRD filings (VAT
/// Summary, TDS, Annex 13), the two whole-organization analytics (Ratio Analysis, Exceptional) and
/// User Log. Annex 5 carries the filter <i>despite</i> being an IRD annex, which is exactly why the
/// pass read all 49 rather than one report per family — a sampled list would have produced the tidy
/// rule "statutory reports have no location" and been wrong (phase-30's find-the-rule lesson, and
/// phase-35a's correction to it: a complete enumeration is not a sample).</para>
///
/// <para><b>What implementing this obliges a query to do</b>, all three asserted by
/// <c>ReportLocationSweepGuardTests</c>: carry a nullable <c>LocationId</c> whose default is null
/// ("All locations", the live default and what every pre-35b caller keeps sending), have its handler
/// apply that filter, and have its handler apply <c>LocationAccessScope.ForReportsAsync</c> — the
/// permission scope, which is a different narrowing for a different reason and must not be confused
/// with the user's own filter.</para>
///
/// <para><b>Not implementing it is a decision with a reason</b>, recorded in that guard's exemption
/// map rather than by the query's silence. Three of this codebase's report screens are exempt for a
/// reason the live census cannot supply, because the row they read carries no location at all: the
/// two migrated tax registers (a cutover import row is deliberately not a document — phase 21c) and
/// System Audit (an <c>Audit</c> row records a command against a (DocumentType, DocumentId) pair and
/// nothing else).</para>
/// </summary>
public interface ILocationFilteredReport
{
    /// <summary>The requested billing location, or null for "All locations".</summary>
    Guid? LocationId { get; }
}
