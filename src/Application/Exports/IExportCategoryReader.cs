using ErpApp.Domain.Exports;

namespace ErpApp.Application.Exports;

/// <summary>
/// One category of FR-2.8's export, read out of the tenant's data as a rectangle of values.
///
/// <para>The same one-implementation-per-enum-member strategy shape as <c>IEntityImporter</c>
/// (Phase 21a), <c>IAlertContentBuilder</c> (Phase 20e) and <c>IGlPostingRule&lt;T&gt;</c>: the
/// processor resolves the whole set from DI and never names a concrete reader, so adding a category
/// is a new class, a new <see cref="ExportCategory"/> member and one DI line.</para>
///
/// <para><b>Every implementation must filter by organizationId by hand.</b> There is no EF global
/// query filter in this codebase, and this feature reads from more tables at once than anything
/// built before it -- which makes tenant isolation the headline test of the phase rather than a
/// formality.</para>
/// </summary>
public interface IExportCategoryReader
{
    ExportCategory Category { get; }

    /// <summary>The worksheet name. Excel caps sheet names at 31 characters and forbids
    /// <c>: \ / ? * [ ]</c>; all five current names are short plain words.</summary>
    string SheetName { get; }

    IReadOnlyList<string> Headers { get; }

    /// <summary>
    /// Reads at most <paramref name="maxRows"/> rows, in a deterministic order, plus the true
    /// unclamped count so truncation can be disclosed rather than hidden.
    /// </summary>
    Task<ExportCategoryResult> ReadAsync(Guid organizationId, int maxRows, CancellationToken cancellationToken);
}

/// <summary>
/// One category's rectangle. <c>object?</c> cells rather than strings so the workbook writer can
/// keep numbers numeric and dates dates -- a spreadsheet whose amounts are text is useless for the
/// arithmetic people open it to do.
/// </summary>
public sealed record ExportCategoryResult(IReadOnlyList<object?[]> Rows, int TotalRowCount)
{
    public bool IsTruncated => Rows.Count < TotalRowCount;
}

/// <summary>
/// The caps this feature enforces, and states plainly (Decision B).
///
/// <para>ClosedXML is not a streaming writer: <c>XLWorkbook</c> materialises every cell of every
/// sheet in memory before a single byte is written, and then <c>SaveAs</c> buffers the whole package
/// again because Kestrel disallows synchronous writes to a live response stream (phase-16c bug #3).
/// Phase 21a met the same constraint from the read side and answered it with a stated 5,000-row cap
/// rather than a pretence of streaming; the write side gets the same honesty.</para>
/// </summary>
public static class ExportLimits
{
    /// <summary>
    /// Rows per category, excluding the header. Five sheets at this cap is a worst case of 125,000
    /// rows in one buffered workbook, which is the largest artifact this library can produce without
    /// putting a server at risk.
    ///
    /// <para>A tenant past the cap still gets a complete, openable file: the category is cut off at
    /// this many rows in its deterministic order, and the truncation is disclosed in three places
    /// (the Summary sheet, the job row's <c>TruncationNotice</c>, and the completion email). Raising
    /// it means moving to a streaming writer (the OpenXml SDK's SAX-style writer), which is the
    /// recorded follow-up if a real tenant ever hits it.</para>
    /// </summary>
    public const int MaxRowsPerCategory = 25_000;
}
