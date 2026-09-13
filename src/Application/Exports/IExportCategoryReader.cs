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
    /// <c>: \ / ? * [ ]</c>; all eight current names are short plain words.</summary>
    string SheetName { get; }

    IReadOnlyList<string> Headers { get; }

    /// <summary>
    /// Whether this category has a date to filter on at all (Phase 38).
    ///
    /// <para><b>Every reader must answer, and the answer is published rather than inferred.</b> A
    /// date range narrows the transactional categories and is meaningless for master data -- a
    /// product is not "in" August. Leaving that implicit would produce the worst version of the
    /// feature: a user who exports one month and sees a full product list cannot tell whether the
    /// filter worked, and one who sees a short list cannot tell whether their catalogue is that
    /// small. So the workbook's Summary sheet prints "not date-filtered" against each sheet that
    /// says false here.</para>
    /// </summary>
    bool IsDateFiltered { get; }

    /// <summary>
    /// Reads at most <paramref name="maxRows"/> rows, in a deterministic order, plus the true
    /// unclamped count so truncation can be disclosed rather than hidden.
    /// </summary>
    /// <param name="range">The requested window. A reader whose <see cref="IsDateFiltered"/> is
    /// false ignores it.</param>
    Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, ExportDateRange range, CancellationToken cancellationToken);
}

/// <summary>
/// The window an export was asked for. <see cref="Unbounded"/> is the phase-21b behaviour and stays
/// the default, because "export my data" with no further thought must keep meaning all of it.
/// </summary>
public readonly record struct ExportDateRange(DateOnly? From, DateOnly? To)
{
    public static ExportDateRange Unbounded => new(null, null);

    public bool IsBounded => From is not null || To is not null;

    /// <summary>Renders the window for the Summary sheet and the completion email.</summary>
    public string Describe() => (From, To) switch
    {
        (null, null) => "All dates",
        ({ } from, null) => $"{from:yyyy-MM-dd} onwards",
        (null, { } to) => $"up to {to:yyyy-MM-dd}",
        ({ } from, { } to) => $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd}",
    };
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
    /// Rows per category, excluding the header.
    ///
    /// <para><b>Raised from 25,000 to 50,000 in Phase 38, on phase 34c's measurement rather than on
    /// taste.</b> 34c exported the 50,000-invoice NFR-5.1 dataset and found the condition for
    /// revisiting this constant already met by specification -- Ledger Transactions lost 88% of its
    /// rows -- and it left the number alone deliberately, because "the phase that first measured it
    /// should hand the decision over with numbers rather than raise a production safety limit on the
    /// strength of a single run". The number it handed over is <b>~2.5 kB of process working set per
    /// row</b>, from a run that produced 280,024 rows in ~700 MB above a 215 MB idle baseline and
    /// completed in 15.8 s.</para>
    ///
    /// <para>50,000 takes that dataset's Contacts sheet from truncated to <i>complete</i> (50,002
    /// rows available) and doubles what its ledger carries, at roughly 350 MB for the whole workbook
    /// -- half of what 34c already ran successfully. The SAX rewrite is still not the move: 34c said
    /// raising the cap comes first, and this is that, with the remaining headroom now bounded by
    /// <see cref="MaxRowsPerWorkbook"/> rather than by multiplying the per-sheet cap by however many
    /// categories exist.</para>
    /// </summary>
    public const int MaxRowsPerCategory = 50_000;

    /// <summary>
    /// Rows across every sheet of one workbook, excluding headers.
    ///
    /// <para><b>This is the cap that actually expresses the memory law</b>, and phase 34c is why it
    /// exists: "25,000 per category was never the right shape", because the real constraint is a
    /// budget for one buffered workbook divided by however many categories happen to be large at
    /// once -- and Phase 38 took the category count from five to eight, which would otherwise have
    /// multiplied the worst case by 1.6 without anybody choosing to. At the measured 2.5 kB per row
    /// this is about 375 MB of working set above baseline, comfortably inside the envelope 34c ran.</para>
    ///
    /// <para>The budget is consumed in <c>ExportJobProcessor</c>'s fixed category order and what is
    /// left is what the next sheet may take, so a workbook that reaches the ceiling truncates its
    /// <i>later</i> sheets and says so per sheet. A user who needs a large late category takes it on
    /// its own -- which is exactly what per-category selection and the date range are for, and is
    /// the honest answer to a cap rather than a bigger number.</para>
    /// </summary>
    public const int MaxRowsPerWorkbook = 150_000;
}
