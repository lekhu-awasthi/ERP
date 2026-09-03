namespace ErpApp.Application.Accounting.Reports;

/// <summary>
/// Phase 26a -- the single place that decides *what* a report's Compare column is compared
/// against (FR-9.1's "Compare" switch, which Phase 8a's three financial statements never built).
///
/// <para>The design rule is the one the roadmap's own 26a bullet states: the comparison window is
/// derived <b>server-side, as a second window over the same handler</b>, and rendered as extra
/// columns on the one response -- never as a second HTTP request the Angular side then has to
/// line up row-by-row. Lining two independent responses up in the browser would mean re-deriving
/// the account list, the ordering and the group rollups client-side, which is exactly the
/// full-set-versus-current-page mistake phase-16c found in four report pages.</para>
///
/// <para>Two shapes, because this codebase's financial statements come in two shapes:</para>
/// <para><b>Range reports</b> (Income Statement: FromDate..ToDate) compare against the
/// <i>same-length period immediately preceding</i> -- a 31-day January compares against the 31
/// days ending the day before it. This is the literal "same-length prior period" the roadmap
/// asks for, and it is unambiguous because the length is given by the request itself.</para>
///
/// <para><b>As-of reports</b> (Trial Balance, Balance Sheet: a single AsOfDate) have no length to
/// reuse, so "same-length prior period" is undefined for them. The comparison is
/// <see cref="PriorYearAsOf"/> -- the same calendar date one year earlier, which is what a
/// comparative balance sheet means everywhere in accounting practice. It is a deliberate choice,
/// not a fallback: see docs/phase-26a-status.md's Decision A, which also records why an explicit
/// user-picked compare date was left as the obvious future extension rather than built now.</para>
///
/// <para>Every Compare-capable DTO echoes the window this class produced (CompareAsOfDate, or
/// CompareFromDate/CompareToDate) so the screen and the .xlsx label the extra columns with the
/// real dates rather than the word "prior" -- a comparison whose period the reader has to guess
/// is worse than no comparison.</para>
/// </summary>
public static class ComparePeriod
{
    /// <summary>
    /// The same-length window ending the day before <paramref name="fromDate"/>. Inclusive on both
    /// ends, matching every range report's own [FromDate, ToDate] convention.
    /// </summary>
    public static (DateOnly FromDate, DateOnly ToDate) SameLengthPrior(DateOnly fromDate, DateOnly toDate)
    {
        var toExclusive = fromDate.AddDays(-1);
        var lengthInDays = toDate.DayNumber - fromDate.DayNumber;
        return (toExclusive.AddDays(-lengthInDays), toExclusive);
    }

    /// <summary>
    /// The same calendar date one year earlier. DateOnly.AddYears already clamps 29 February to 28
    /// February in a non-leap year, which is the conventional answer and the only one that keeps
    /// the comparison a real date.
    /// </summary>
    public static DateOnly PriorYearAsOf(DateOnly asOfDate) => asOfDate.AddYears(-1);
}
