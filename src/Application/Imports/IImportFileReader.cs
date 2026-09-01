namespace ErpApp.Application.Imports;

/// <summary>
/// Turns an uploaded workbook into an <see cref="ImportSheet"/>. Implemented in Infrastructure by
/// ClosedXmlImportFileReader (Decision D) -- the interface exists so the background runner, which
/// lives in Infrastructure, does not force a spreadsheet dependency into Application, and so the
/// importers stay unit-testable without a file.
///
/// <para>Implementations must throw <see cref="ImportFileFormatException"/> for anything the user
/// could plausibly have done wrong (not a workbook, corrupt, no sheets, over the row cap), never a
/// raw provider exception -- the processor turns that exception's message into the job's own
/// FailureReason and it is read by a human.</para>
/// </summary>
public interface IImportFileReader
{
    Task<ImportSheet> ReadAsync(Stream content, CancellationToken cancellationToken = default);
}

/// <summary>A whole-file problem: unreadable, empty, or too large. Fails the job, not a row.</summary>
public sealed class ImportFileFormatException(string message) : Exception(message);

/// <summary>
/// Caps, stated rather than implied. ClosedXML materialises an entire workbook in memory and is not
/// a streaming reader, so the honest answer to NFR-5.1 for this phase is a bounded file, not a
/// pretence of streaming: a rejected 6,000-row upload with a clear message beats an accepted one
/// that exhausts the server. Splitting a larger migration into batches is the documented workflow.
/// </summary>
public static class ImportLimits
{
    /// <summary>Data rows, excluding the header.</summary>
    public const int MaxDataRows = 5_000;

    public const long MaxFileSizeBytes = 10L * 1024 * 1024;
}
