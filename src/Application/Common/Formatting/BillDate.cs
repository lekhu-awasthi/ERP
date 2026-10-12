using ErpApp.Domain.Common;

namespace ErpApp.Application.Common.Formatting;

/// <summary>
/// Phase 71 -- how a <b>bill's</b> date is written: Bikram Sambat first, with the Gregorian date beside it, labelled,
/// <b>whatever the viewer's calendar toggle says</b>. <c>2026-09-24</c> becomes <c>2083-06-08 BS (2026-09-24 AD)</c>.
///
/// <para><b>Why a bill is not <see cref="RequestCalendar"/>.</b> The toggle is a display preference, so a bill printed
/// under the default, or emailed by a job that sends no header, came out AD only. A Nepali tax document is dated in
/// BS by practice, and the return it lands in is a BS month (VAT Rules 2053, Rule 26). The statutory forms
/// (Schedules 5 and 6, the e-billing procedure's Annex 6) name no calendar, so this is the user's 2026-10-08 rule
/// and not a quoted clause; <c>docs/phase-71-status.md</c> § 0 has the sources. The AD date stays beside it for a
/// reader who does not use BS (an export buyer); the reference product calls the same choice "Both".</para>
///
/// <para>Both halves keep the <c>yyyy-MM-dd</c> shape <see cref="RequestCalendar"/> uses, so a bill reads like every
/// other server-rendered date. A date outside the BS table (BS 2000..2092) prints <c>2040-01-01 AD</c>: still
/// labelled, never a guessed BS date.</para>
/// </summary>
public static class BillDate
{
    public static string Format(DateOnly date)
    {
        var ad = Ad(date);
        return Bs(date) is { } bs ? $"{bs} BS ({ad} AD)" : $"{ad} AD";
    }

    /// <summary>The BS half alone, or null outside the table -- a register's own column (phase 71).</summary>
    public static string? Bs(DateOnly date) =>
        BsCalendar.FromGregorian(date) is { } bs ? BsCalendar.Format(bs) : null;

    public static string Ad(DateOnly date) => date.ToString("yyyy-MM-dd");

    /// <summary>Phase 89 -- a statutory export's period, by the same rule: <c>2083-04-01 to 2083-06-26 BS (2026-07-17 to
    /// 2026-10-12 AD)</c>. AD alone when either end is outside the BS table.</summary>
    public static string FormatRange(DateOnly from, DateOnly to)
    {
        var ad = $"{Ad(from)} to {Ad(to)}";
        return Bs(from) is { } bsFrom && Bs(to) is { } bsTo ? $"{bsFrom} to {bsTo} BS ({ad} AD)" : $"{ad} AD";
    }
}
