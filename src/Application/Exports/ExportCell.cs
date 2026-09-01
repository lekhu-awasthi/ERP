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
}
