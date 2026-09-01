using System.Globalization;
using System.Text;
using ErpApp.Application.Common.Email;
using ErpApp.Application.Common.Jobs;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Storage;
using ErpApp.Domain.Common;
using ErpApp.Domain.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErpApp.Application.Exports;

/// <summary>
/// Runs one full-tenant data export. See <see cref="IExportJobProcessor"/> for the runner/decider
/// split and docs/phase-21b-status.md for Decisions A-F.
///
/// <para><b>Why this is so much simpler than Phase 21a's importer, in one paragraph.</b> An export
/// is <i>idempotent</i>: re-running it regenerates a file and changes nothing about the tenant. That
/// single property deletes most of 21a's machinery. There is no per-row ledger and no
/// claim-under-a-unique-index, because nothing can be created twice. There is no
/// <c>IJobActingUser</c>, because the job only reads and reads directly through org-filtered
/// queries rather than permission-gated MediatR requests -- so Phase 20e's "a background job needs
/// no ambient identity" default, which 21a had to give up, is available again and taken (Decision
/// D). And a run that dies half-way needs no resume logic: another runner re-claims the job on a
/// stale heartbeat and rebuilds the workbook from scratch.</para>
///
/// <para><b>The artifact and the terminal status commit together.</b> The workbook is built into a
/// buffer, saved to <c>IFileStorage</c>, and only then is the key written alongside
/// <c>Completed</c> in one <c>SaveChangesAsync</c>. A process that dies at any point before that
/// leaves a job that is still Running with no <c>StorageKey</c>, which the UI shows as in-progress
/// and never as a download. The residue is at worst one orphaned blob (saved, never committed) --
/// which is a cost, not a correctness failure, and one the retention sweep's export-side sibling
/// cannot see. Deliberately accepted rather than paid for with a two-phase protocol.</para>
/// </summary>
public sealed class ExportJobProcessor(
    IAppDbContext db,
    IEnumerable<IExportCategoryReader> categoryReaders,
    IExportWorkbookWriter workbookWriter,
    IFileStorage fileStorage,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<ExportJobProcessor> logger) : IExportJobProcessor
{
    /// <summary>How long a Running job may go without a heartbeat before another runner treats it as
    /// abandoned. Matches the importer's lease for the same reasons: long enough that a slow
    /// category cannot trigger a false steal, short enough that a crashed export is not stranded for
    /// a shift.</summary>
    private static readonly TimeSpan RunnerLease = TimeSpan.FromMinutes(2);

    private const string SummarySheetName = "Summary";

    /// <summary>The sentence this whole feature turns on -- see Decision A. FR-2.8 says "backup",
    /// this codebase has no restore path and none is planned, so the artifact says what it actually
    /// is, in the file itself and not only in a tooltip somebody may never hover.</summary>
    private const string NotABackupNotice =
        "This file is a human-readable EXPORT of your data, not a restorable backup. "
        + "It cannot be uploaded back into ErpApp to recreate this organization.";

    /// <summary>Fixed sheet order, so two exports of the same tenant differ only where the data
    /// does. Follows FR-2.8's own listing order.</summary>
    private static readonly ExportCategory[] CategoryOrder =
    [
        ExportCategory.Products,
        ExportCategory.Contacts,
        ExportCategory.ChartOfAccounts,
        ExportCategory.LedgerTransactions,
        ExportCategory.StockMovements,
    ];

    /// <summary>The number of sheets a job promises, used for its progress bar. Public because the
    /// enqueue command stamps it on the job before any runner has seen it.</summary>
    public static int CategoryCount => CategoryOrder.Length;

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var job = await ClaimNextJobAsync(cancellationToken);
        if (job is null)
        {
            return false;
        }

        logger.LogInformation("Export job {ExportJobId} claimed.", job.Id);

        try
        {
            await RunAsync(job, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown, not a failure. The job stays Running with a heartbeat that will go
            // stale, and the next process to start regenerates it from scratch.
            logger.LogInformation("Export job {ExportJobId} interrupted by shutdown; it will restart.", job.Id);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Export job {ExportJobId} failed.", job.Id);
            await FailAsync(job, ex.Message, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Deletes expired artifacts for both job kinds' <i>export</i> side (Decision E). The import
    /// side is swept by <c>ImportJobProcessor.SweepAsync</c>, on its own runner's tick.
    ///
    /// <para>The row is kept and stamped rather than deleted: a user who comes back to a week-old
    /// export must be told it expired, not handed a download button that 404s or, worse, a silently
    /// missing history entry.</para>
    /// </summary>
    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var expired = await db.ExportJobs
            .Where(j => j.ArtifactPurgedAt == null && j.ExpiresAt != null && j.ExpiresAt < now)
            .OrderBy(j => j.ExpiresAt)
            .Take(JobArtifactRetention.SweepBatchSize)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return;
        }

        foreach (var job in expired)
        {
            // The blob goes first, then the row is stamped. A crash between the two re-sweeps the
            // same row next tick and deletes an already-deleted file, which IFileStorage treats as a
            // no-op -- the safe ordering. The reverse would strand the file forever.
            if (job.StorageKey is { } key)
            {
                try
                {
                    await fileStorage.DeleteAsync(key, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(
                        ex, "Could not delete the artifact of expired export job {ExportJobId}.", job.Id);
                    continue;
                }
            }

            job.MarkArtifactPurged(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Retention purged {PurgedCount} expired export artifact(s).", expired.Count);
    }

    /// <summary>
    /// Takes the oldest job that is either Queued or Running-but-abandoned. Claiming is advisory,
    /// not exclusive -- see <see cref="ExportJob.HeartbeatAt"/>'s remarks for why there is no
    /// concurrency token, and why two runners racing on one export is wasteful rather than wrong.
    /// </summary>
    private async Task<ExportJob?> ClaimNextJobAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var abandonedBefore = now - RunnerLease;

        var job = await db.ExportJobs
            .Where(j => j.Status == ExportJobStatus.Queued
                        || (j.Status == ExportJobStatus.Running
                            && (j.HeartbeatAt == null || j.HeartbeatAt < abandonedBefore)))
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return null;
        }

        job.Claim(now);
        await db.SaveChangesAsync(cancellationToken);
        return job;
    }

    private async Task RunAsync(ExportJob job, CancellationToken cancellationToken)
    {
        var readers = ResolveReaders();

        var sheets = new List<ExportWorkbookSheet>();
        var summaryRows = new List<object?[]>();
        var truncated = new List<string>();
        var totalRows = 0;
        var processed = 0;

        foreach (var reader in readers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsCancellationRequestedAsync(job.Id, cancellationToken))
            {
                // Nothing has been written to storage yet, so a cancelled export leaves no artifact
                // and no partial file -- the one place cancellation is simpler here than for an
                // import, whose already-created rows must stay.
                job.MarkCancelled(timeProvider.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Export job {ExportJobId} cancelled by the user.", job.Id);
                await NotifyAsync(job, cancellationToken);
                return;
            }

            var result = await reader.ReadAsync(
                job.OrganizationId, ExportLimits.MaxRowsPerCategory, cancellationToken);

            sheets.Add(new ExportWorkbookSheet(reader.SheetName, reader.Headers, result.Rows));

            summaryRows.Add(
            [
                reader.SheetName,
                result.Rows.Count,
                result.TotalRowCount,
                result.IsTruncated ? "Yes" : "No",
            ]);

            if (result.IsTruncated)
            {
                truncated.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{reader.SheetName} ({result.Rows.Count:N0} of {result.TotalRowCount:N0} rows)"));
            }

            totalRows += result.Rows.Count;
            processed++;

            job.SetProgress(processed, totalRows, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        var context = await ReadContextAsync(job, cancellationToken);
        var truncationNotice = truncated.Count == 0 ? null : string.Join("; ", truncated);

        // The Summary sheet leads the workbook, so the first thing anyone sees when they open the
        // file is what it is, who made it, and whether anything was cut off.
        sheets.Insert(
            0,
            new ExportWorkbookSheet(
                SummarySheetName,
                ["Sheet", "Rows Exported", "Rows Available", "Truncated"],
                summaryRows,
                BuildPreamble(context, truncationNotice)));

        var fileName = BuildFileName(context.OrganizationName, timeProvider.GetUtcNow());

        using var buffer = new MemoryStream();
        await workbookWriter.WriteAsync(new ExportWorkbook(sheets), buffer, cancellationToken);
        buffer.Position = 0;
        var size = buffer.Length;

        var storageKey = await fileStorage.SaveAsync(buffer, fileName, cancellationToken);

        job.MarkCompleted(
            timeProvider.GetUtcNow(), storageKey, fileName, size, truncationNotice, JobArtifactRetention.Period);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Export job {ExportJobId} finished: {SheetCount} sheet(s), {RowCount} row(s), {ByteCount} bytes.",
            job.Id, sheets.Count, totalRows, size);

        await NotifyAsync(job, cancellationToken);
    }

    /// <summary>Ordered by <see cref="CategoryOrder"/> and verified complete, so a category whose DI
    /// line was forgotten fails loudly at run time instead of silently producing a workbook missing
    /// a sheet that FR-2.8 names.</summary>
    private IReadOnlyList<IExportCategoryReader> ResolveReaders()
    {
        var byCategory = categoryReaders.ToDictionary(r => r.Category);

        return
        [
            .. CategoryOrder.Select(category =>
                byCategory.TryGetValue(category, out var reader)
                    ? reader
                    : throw new InvalidOperationException(
                        $"No IExportCategoryReader is registered for {category}."))
        ];
    }

    private async Task<ExportContext> ReadContextAsync(ExportJob job, CancellationToken cancellationToken)
    {
        var organizationName = await db.Organizations
            .Where(o => o.Id == job.OrganizationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Organization";

        var initiator = await db.Users
            .Where(u => u.Id == job.InitiatedByUserId)
            .Select(u => new { u.FullName, u.Email })
            .FirstOrDefaultAsync(cancellationToken);

        return new ExportContext(organizationName, initiator?.FullName ?? string.Empty, initiator?.Email);
    }

    private IReadOnlyList<string> BuildPreamble(ExportContext context, string? truncationNotice)
    {
        var lines = new List<string>
        {
            $"{context.OrganizationName} - data export",
            NotABackupNotice,
            $"Generated: {ExportCellText(timeProvider.GetUtcNow())} (Nepal time)",
        };

        if (!string.IsNullOrWhiteSpace(context.InitiatedByName))
        {
            lines.Add($"Generated by: {context.InitiatedByName}");
        }

        if (truncationNotice is not null)
        {
            lines.Add(
                $"TRUNCATED: some categories exceeded the {ExportLimits.MaxRowsPerCategory:N0}-row per-sheet "
                + $"limit and were cut off - {truncationNotice}.");
        }

        return lines;
    }

    private static string ExportCellText(DateTimeOffset instant) =>
        NepalTime.ToLocal(instant).ToString("yyyy-MM-dd HH:mm");

    /// <summary>
    /// The organization name is folded into the file name because a user with several tenants will
    /// have several of these in one Downloads folder. It is reduced to letters, digits and hyphens
    /// first -- an organization is free to be called "Acme / Kathmandu (P.) Ltd.", and that string
    /// reaches a Content-Disposition header and then a file system.
    /// </summary>
    private static string BuildFileName(string organizationName, DateTimeOffset now)
    {
        var slug = new StringBuilder();
        foreach (var ch in organizationName)
        {
            if (char.IsLetterOrDigit(ch))
            {
                slug.Append(ch);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var trimmed = slug.ToString().Trim('-');
        if (trimmed.Length > 40)
        {
            trimmed = trimmed[..40].TrimEnd('-');
        }

        var stem = trimmed.Length == 0 ? "DataExport" : $"DataExport_{trimmed}";
        return $"{stem}_{NepalTime.ToLocal(now):yyyy-MM-dd_HHmm}.xlsx";
    }

    private async Task<bool> IsCancellationRequestedAsync(Guid jobId, CancellationToken cancellationToken)
    {
        // AsNoTracking, because the job entity in this context's tracker still holds the value it
        // had when it was claimed -- the cancel command ran in a different scope entirely.
        return await db.ExportJobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.CancellationRequested)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task FailAsync(ExportJob job, string reason, CancellationToken cancellationToken)
    {
        job.MarkFailed(timeProvider.GetUtcNow(), reason);
        await db.SaveChangesAsync(cancellationToken);
        await NotifyAsync(job, cancellationToken);
    }

    /// <summary>
    /// <b>NFR-4.3's "with the user notified on completion", reusing Phase 21a's Decision E
    /// unchanged.</b> The screen polls and is the primary answer for a user who stayed; this covers
    /// the one who did not. The address is the initiating user's own registered email, looked up
    /// server-side from their id -- nothing about the recipient is caller-supplied, so this reopens
    /// none of Phase 20e's free-text-recipient egress concerns.
    ///
    /// <para>The mail deliberately carries no attachment and no link to a token-bearing URL: a
    /// full-tenant export must only ever leave the system through the authenticated,
    /// permission-checked download endpoint (Decision F). Failure to notify never fails the job.</para>
    /// </summary>
    private async Task NotifyAsync(ExportJob job, CancellationToken cancellationToken)
    {
        try
        {
            var email = await db.Users
                .Where(u => u.Id == job.InitiatedByUserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(email))
            {
                return;
            }

            var subject = $"Data export {job.Status.ToString().ToLowerInvariant()}";
            var body = job.Status switch
            {
                ExportJobStatus.Completed =>
                    $"Your data export is ready: {job.FileName}\r\n\r\n"
                        + $"Rows exported: {job.TotalRowCount:N0} across {job.ProcessedCategoryCount} sheet(s)\r\n"
                        + (job.TruncationNotice is null
                            ? string.Empty
                            : $"Truncated at the {ExportLimits.MaxRowsPerCategory:N0}-row per-sheet limit: "
                                + $"{job.TruncationNotice}\r\n")
                        + "\r\nOpen the Import / Export screen to download it. "
                        + $"It will be deleted automatically after {JobArtifactRetention.Period.TotalDays:N0} days.\r\n\r\n"
                        + NotABackupNotice,
                ExportJobStatus.Failed =>
                    $"Your data export could not be produced.\r\n\r\n{job.FailureReason}",
                _ => "Your data export was cancelled. No file was produced.",
            };

            await emailSender.SendAsync(email, subject, body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not notify the initiator of export job {ExportJobId}.", job.Id);
        }
    }

    private sealed record ExportContext(string OrganizationName, string InitiatedByName, string? InitiatedByEmail);
}
