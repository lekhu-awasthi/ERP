using ErpApp.Domain.Common;

namespace ErpApp.Application.Exports;

/// <summary>Cell-formatting helpers shared by every <see cref="IExportCategoryReader"/>.</summary>
internal static class ExportCell
{
    /// <summary>
    /// Renders a stored UTC instant on the <b>Nepal wall clock</b> (UTC+05:45), which is the only
    /// clock this product has: <c>Organization</c> carries no timezone field and CLAUDE.md's
    /// standing rule is that anything dated for a tenant is computed through
    /// <see cref="NepalTime"/>. The :45 offset makes the failure mode subtle -- between 18:15 and
    /// 24:00 UTC the Nepal calendar date is already tomorrow -- so a raw UTC stamp in an export
    /// would silently show a Nepali accountant the wrong day for every evening transaction.
    ///
    /// <para>Written as text rather than a typed date so the offset cannot be lost or re-interpreted
    /// by whatever locale opens the file.</para>
    /// </summary>
    public static string LocalTimestamp(DateTimeOffset instant) =>
        NepalTime.ToLocal(instant).ToString("yyyy-MM-dd HH:mm");

    /// <summary>
    /// Renders a stored business date (Phase 38's document categories are the first export rows that
    /// carry one).
    ///
    /// <para><b>No <see cref="NepalTime"/> conversion here, deliberately.</b> A
    /// <c>DateOnly</c> in this codebase is already the tenant's own business date -- it was typed on
    /// a form, never derived from an instant -- so shifting it by an offset would move an invoice
    /// dated Shrawan 1 onto Ashad 32. The two helpers look alike and must not be used
    /// interchangeably: <see cref="LocalTimestamp"/> is for stamps the server made,
    /// this is for dates the user chose.</para>
    ///
    /// <para>ISO text, not a typed date, for the same reason as above: an unambiguous string cannot
    /// be re-read as a US date by whichever locale opens the file. Dates a Nepali user should read
    /// in BS are a presentation concern of the screens (phase 23), and an export is a machine-facing
    /// artifact.</para>
    /// </summary>
    public static string LocalDate(DateOnly date) => date.ToString("yyyy-MM-dd");
}
