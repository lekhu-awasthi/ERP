using ErpApp.Domain.Common;

namespace ErpApp.Application.Common.Formatting;

/// <summary>The calendar a response's <b>business dates</b> are rendered in. Phase 23 stores every
/// date in AD and converts at the client edge; this is the same choice carried to output the client
/// never touches.</summary>
public enum CalendarFormat
{
    /// <summary>Gregorian. The default, and what every server-rendered date was before Phase 27b.</summary>
    Ad = 0,

    /// <summary>Bikram Sambat, via <see cref="BsCalendar"/> (BS 2000..2092).</summary>
    Bs = 1,
}

/// <summary>
/// Phase 27b -- closes phase-23 <b>Decision A</b>'s stated limitation: "Phase 20d's print/PDF
/// pipeline and Phase 16c/21b's <c>.xlsx</c> exports both render dates server-side, so they remain
/// AD regardless of the user's setting."
///
/// <para>The preference itself still lives where Phase 23 put it -- <c>DatePreferenceService</c>'s
/// browser storage, per-user and per-device. What is new is that the client now sends it, as the
/// <c>X-Calendar</c> request header, and <c>CalendarPreferenceMiddleware</c> (Api) parks it here for
/// the duration of the request.</para>
///
/// <para><b>Why an ambient <see cref="AsyncLocal{T}"/> rather than a parameter or an injected
/// service</b>, stated plainly because it is the one deliberate exception in this codebase to
/// "dependencies go through the constructor". The consumers are <c>ReportSpreadsheetExporter</c>
/// and its Phase 26c partial -- <b>static</b> classes with roughly forty public export methods,
/// each called from exactly one endpoint. Threading a <c>CalendarFormat</c> through all of them
/// would touch every signature and every call site to deliver one value that is constant for the
/// whole request, and would still not reach inside the <c>Results.Stream</c> callbacks where the
/// workbook is actually built. This is the same category as <c>CultureInfo.CurrentCulture</c>:
/// ambient formatting context, one writer (the middleware), read-only everywhere else.</para>
///
/// <para><b>Scope of the conversion.</b> Business dates only -- exactly phase-23 Decision A's own
/// boundary. A <see cref="DateOnly"/> answers "what date does this document bear" and converts; an
/// audit timestamp (<c>CreatedAt</c>, <c>ApprovedAt</c>, <c>OccurredAt</c>) answers "when did this
/// happen", carries a time of day, and stays AD in both calendars. Download file names also stay
/// AD, so two exports of the same report still sort together on disk; a <c>-BS</c> marker in the
/// name says which calendar is inside.</para>
///
/// <para><b>Out-of-range dates fall back to the AD rendering</b> rather than throwing or guessing,
/// matching <c>NepaliDatePipe</c>'s behaviour on the client for the same reason: a visibly-AD date
/// is honest, a wrong BS date is not.</para>
/// </summary>
public static class RequestCalendar
{
    private static readonly AsyncLocal<CalendarFormat> CurrentCalendar = new();

    /// <summary>The calendar for the request in flight. Defaults to <see cref="CalendarFormat.Ad"/>
    /// everywhere nothing has set it -- background jobs, tests, and any client that does not send
    /// the header -- so behaviour is unchanged unless a caller asks for BS.</summary>
    public static CalendarFormat Current
    {
        get => CurrentCalendar.Value;
        set => CurrentCalendar.Value = value;
    }

    /// <summary>The header the client sends. Values are the <see cref="CalendarFormat"/> member
    /// names, case-insensitively; anything else is treated as AD rather than rejected -- a
    /// malformed preference header must never fail an export.</summary>
    public const string HeaderName = "X-Calendar";

    public static CalendarFormat Parse(string? headerValue) =>
        string.Equals(headerValue, "BS", StringComparison.OrdinalIgnoreCase) ? CalendarFormat.Bs : CalendarFormat.Ad;

    /// <summary>`2026-09-01` -> `2026-09-01` in AD, `2083-05-16` in BS. The <c>yyyy-MM-dd</c> shape
    /// is kept in both calendars so a spreadsheet column still sorts chronologically; the client's
    /// own <c>NepaliDatePipe</c> reorders to <c>dd-MM-yyyy</c> for screen display, which is a
    /// reading-comfort choice a sortable cell should not inherit.</summary>
    public static string Format(DateOnly date) => Format(date, Current);

    public static string Format(DateOnly date, CalendarFormat calendar)
    {
        if (calendar == CalendarFormat.Ad)
        {
            return date.ToString("yyyy-MM-dd");
        }

        var bs = BsCalendar.FromGregorian(date);
        return bs is null ? date.ToString("yyyy-MM-dd") : BsCalendar.Format(bs.Value);
    }

    /// <summary>Nullable convenience for the many report rows whose date column is optional.</summary>
    public static string? Format(DateOnly? date) => date is null ? null : Format(date.Value);

    /// <summary>The marker appended to a download's file name when its dates are BS -- see this
    /// class's own note on why the name's own dates stay AD.</summary>
    public static string FileNameMarker => Current == CalendarFormat.Bs ? "-BS" : string.Empty;

    /// <summary>The disclosure line a PDF footer carries so a printed page says which calendar its
    /// dates are in. Empty in AD: every date this app rendered before Phase 27b was AD, and adding
    /// a footer to unchanged output would be noise.</summary>
    public static string? DisclosureLine =>
        Current == CalendarFormat.Bs ? "Dates shown in Bikram Sambat (BS)" : null;
}
