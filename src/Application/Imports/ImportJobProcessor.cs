using ErpApp.Application.Common.Email;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using ErpApp.Domain.Imports;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ErpApp.Application.Imports;

/// <summary>
/// Runs one bulk-import job. See <see cref="IImportJobProcessor"/> for the runner/decider split and
/// docs/phase-21a-status.md for Decisions A-E.
///
/// <para><b>Decision C, the semantics of a job that is not idempotent, in one paragraph.</b> The
/// unit of at-most-once here is the <i>row</i>, not the job: an <see cref="ImportJobRow"/> is
/// inserted and committed under a unique index on (job, row number) <b>before</b> the create/update
/// command is sent -- the same claim-then-act ordering as Phase 20e's AlertSendLog, and for the same
/// reason. A process that dies at row 500 of 1,000 leaves 500 claimed rows; the resumed run skips
/// every one of them and continues at 501, so <b>no row is ever created twice</b>. The price is the
/// mirror image: the row being processed at the instant of the crash has an unknown outcome, and is
/// reported as failed-interrupted rather than guessed at. The alternatives were rejected
/// deliberately -- one all-or-nothing transaction cannot express FR-2.9's required partial success
/// and would hold a write transaction open across thousands of commands, and requiring a fresh
/// upload after any crash throws away work the user can see succeeded.</para>
///
/// <para><b>Two contexts, on purpose.</b> The job's own bookkeeping (the ImportJob row, every
/// ImportJobRow) is written through this scope's <see cref="IAppDbContext"/>. Each row's actual
/// command runs in its <i>own</i> DI scope with its own context. That is not ceremony: a command
/// that throws part-way can leave its change tracker holding a half-built entity, and if the ledger
/// shared that tracker, the very SaveChanges that records the failure would try to flush the failed
/// work with it. Separating them means recording an outcome can never fail because of the thing it
/// is recording.</para>
/// </summary>
public sealed class ImportJobProcessor(
    IAppDbContext db,
    IFileStorage fileStorage,
    IImportFileReader fileReader,
    IServiceScopeFactory scopeFactory,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<ImportJobProcessor> logger) : IImportJobProcessor
{
    /// <summary>How long a Running job may go without a heartbeat before another runner treats it as
    /// abandoned and resumes it. Long enough that a slow row cannot trigger a false steal, short
    /// enough that a crashed import is not stranded for a shift.</summary>
    private static readonly TimeSpan RunnerLease = TimeSpan.FromMinutes(2);

    /// <summary>Rows between cancellation re-reads and heartbeat/progress writes. Checking every row
    /// would add a round trip per row for a flag that changes at most once per job.</summary>
    private const int ProgressInterval = 10;

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var job = await ClaimNextJobAsync(cancellationToken);
        if (job is null)
        {
            return false;
        }

        logger.LogInformation(
            "Import job {ImportJobId} claimed ({EntityType}, {Mode}).", job.Id, job.EntityType, job.Mode);

        try
        {
            await RunAsync(job, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown, not a failure. The job stays Running with a heartbeat that will go
            // stale, and the next process to start resumes it from the first unclaimed row.
            logger.LogInformation("Import job {ImportJobId} interrupted by shutdown; it will resume.", job.Id);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import job {ImportJobId} failed.", job.Id);
            await FailAsync(job, ex.Message, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Takes the oldest job that is either Queued or Running-but-abandoned.
    ///
    /// <para><b>Claiming is advisory, not exclusive</b>, and deliberately so -- see
    /// <see cref="ImportJob.HeartbeatAt"/>'s remarks. Two runners can both take one job; neither can
    /// process the same <i>row</i> twice, because that is guarded by ImportJobRow's unique index.
    /// An optimistic-concurrency token here was tried and removed: it made the user's own cancel
    /// command collide with the runner's progress writes.</para>
    /// </summary>
    private async Task<ImportJob?> ClaimNextJobAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var abandonedBefore = now - RunnerLease;

        var job = await db.ImportJobs
            .Where(j => j.Status == ImportJobStatus.Queued
                        || (j.Status == ImportJobStatus.Running
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

    private async Task RunAsync(ImportJob job, CancellationToken cancellationToken)
    {
        var importer = ResolveImporter(job.EntityType);

        var sheet = await ReadSheetAsync(job, cancellationToken);
        if (sheet is null)
        {
            return;
        }

        var columnIndexes = ImportRowReader.BuildColumnIndexes(
            [.. sheet.Headers.Select(ImportRowReader.Normalize)]);

        var missing = importer.Template.Columns
            .Where(c => c.Required && !columnIndexes.ContainsKey(c.Name))
            .Select(c => c.Name)
            .ToList();

        if (missing.Count > 0)
        {
            // Fail fast with the column names rather than letting every row throw the same
            // "'Product Name' is required" -- a header mismatch is one mistake, not N.
            await FailAsync(
                job,
                $"The file's columns do not match the {job.EntityType} template. Missing: {string.Join(", ", missing)}. "
                    + "Download the template and keep its column headers.",
                cancellationToken);
            return;
        }

        var dataRows = sheet.Rows.Where(r => !r.IsBlank).ToList();
        if (dataRows.Count == 0)
        {
            await FailAsync(job, "The file contains no data rows.", cancellationToken);
            return;
        }

        job.SetTotalRowCount(dataRows.Count);
        await db.SaveChangesAsync(cancellationToken);

        var alreadyClaimed = await db.ImportJobRows
            .Where(r => r.ImportJobId == job.Id)
            .Select(r => r.RowNumber)
            .ToListAsync(cancellationToken);

        var claimedRowNumbers = alreadyClaimed.ToHashSet();
        if (claimedRowNumbers.Count > 0)
        {
            logger.LogInformation(
                "Import job {ImportJobId} is resuming; {ClaimedCount} row(s) were already processed.",
                job.Id, claimedRowNumbers.Count);
        }

        var cancelledByUser = false;
        var sinceLastProgressWrite = 0;

        foreach (var dataRow in dataRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sinceLastProgressWrite == 0 || sinceLastProgressWrite >= ProgressInterval)
            {
                if (await IsCancellationRequestedAsync(job.Id, cancellationToken))
                {
                    cancelledByUser = true;
                    break;
                }

                await WriteProgressAsync(job, cancellationToken);
                sinceLastProgressWrite = 0;
            }

            sinceLastProgressWrite++;

            if (!claimedRowNumbers.Add(dataRow.RowNumber))
            {
                continue;
            }

            await ProcessRowAsync(job, importer.EntityType, columnIndexes, dataRow, cancellationToken);
        }

        await FinalizeAsync(job, cancelledByUser, cancellationToken);
    }

    /// <summary>
    /// The claim-then-act core. The ledger row is inserted and committed first; only then is the
    /// command sent, in a scope of its own that has assumed the initiating user's identity.
    /// </summary>
    private async Task ProcessRowAsync(
        ImportJob job,
        ImportEntityType entityType,
        IReadOnlyDictionary<string, int> columnIndexes,
        ImportSheetRow dataRow,
        CancellationToken cancellationToken)
    {
        var ledgerRow = ImportJobRow.Claim(job.Id, job.OrganizationId, dataRow.RowNumber);
        db.ImportJobRows.Add(ledgerRow);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Another runner claimed this row (the unique index rejected the insert). Skipping is
            // the correct outcome; detaching keeps the shared tracker usable for the next row.
            logger.LogInformation(
                ex, "Row {RowNumber} of import job {ImportJobId} was already claimed; skipping.",
                dataRow.RowNumber, job.Id);
            db.ImportJobRows.Entry(ledgerRow).State = EntityState.Detached;
            return;
        }

        using var rowScope = scopeFactory.CreateScope();

        // Decision B: the acting identity is established here, on a scope this method just created,
        // and nowhere else. See IJobActingUser for why this cannot leak into an HTTP request.
        rowScope.ServiceProvider.GetRequiredService<IJobActingUser>().Assume(job.InitiatedByUserId);

        var importer = rowScope.ServiceProvider
            .GetServices<IEntityImporter>()
            .Single(i => i.EntityType == entityType);

        try
        {
            var result = await importer.ApplyAsync(
                job.OrganizationId, job.Mode, new ImportRowReader(columnIndexes, dataRow), cancellationToken);

            ledgerRow.MarkSucceeded(result.TargetId, result.TargetCode);
        }
        catch (ForbiddenException ex)
        {
            // Permission was revoked between enqueue and execution (or never covered this entity
            // type). That is a whole-job condition, not a row's fault: every remaining row would
            // fail identically, so the job stops rather than producing N copies of one message.
            ledgerRow.MarkFailed(null, ex.Message);
            await db.SaveChangesAsync(cancellationToken);
            throw new ImportJobAbortedException(
                $"The user who started this import no longer has permission to perform it: {ex.Message}");
        }
        catch (ImportRowException ex)
        {
            ledgerRow.MarkFailed(ex.ColumnName, ex.Message);
        }
        catch (ValidationException ex)
        {
            // Unpacked rather than taking ex.Message, whose multi-line "Validation failed:\n -- X:"
            // form reads badly in a results grid. FluentValidation's PropertyName is the command's
            // property, which matches the template column for most fields and is close enough to be
            // worth showing when it does not (it points at the right value either way).
            var first = ex.Errors.FirstOrDefault();
            ledgerRow.MarkFailed(
                first?.PropertyName,
                string.Join(" ", ex.Errors.Select(e => e.ErrorMessage)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything else a Create/Update handler threw -- NotFound, Conflict, or a provider
            // error. One bad row must never take the job down.
            ledgerRow.MarkFailed(null, ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private IEntityImporter ResolveImporter(ImportEntityType entityType)
    {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetServices<IEntityImporter>().SingleOrDefault(i => i.EntityType == entityType)
            ?? throw new InvalidOperationException($"No IEntityImporter is registered for {entityType}.");
    }

    /// <summary>Returns null (job already marked Failed) when the file cannot be turned into a sheet.</summary>
    private async Task<ImportSheet?> ReadSheetAsync(ImportJob job, CancellationToken cancellationToken)
    {
        try
        {
            await using var content = await fileStorage.OpenReadAsync(job.StorageKey, cancellationToken);
            return await fileReader.ReadAsync(content, cancellationToken);
        }
        catch (ImportFileFormatException ex)
        {
            await FailAsync(job, ex.Message, cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FailAsync(job, $"The uploaded file could not be read: {ex.Message}", cancellationToken);
            return null;
        }
    }

    private async Task<bool> IsCancellationRequestedAsync(Guid jobId, CancellationToken cancellationToken)
    {
        // AsNoTracking, because the job entity in this context's tracker still holds the value it
        // had when it was claimed -- the cancel command ran in a different scope entirely.
        return await db.ImportJobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.CancellationRequested)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task WriteProgressAsync(ImportJob job, CancellationToken cancellationToken)
    {
        await ApplyCountsAsync(job, cancellationToken);
        job.Heartbeat(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Counts are recomputed from the ledger rather than incremented in memory, so a
    /// resumed job reports totals across both runs instead of restarting from zero.</summary>
    private async Task ApplyCountsAsync(ImportJob job, CancellationToken cancellationToken)
    {
        var counts = await db.ImportJobRows
            .Where(r => r.ImportJobId == job.Id)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var succeeded = counts.SingleOrDefault(c => c.Status == ImportJobRowStatus.Succeeded)?.Count ?? 0;
        var failed = counts.SingleOrDefault(c => c.Status == ImportJobRowStatus.Failed)?.Count ?? 0;
        var pending = counts.SingleOrDefault(c => c.Status == ImportJobRowStatus.Pending)?.Count ?? 0;

        job.SetProgress(succeeded + failed + pending, succeeded, failed);
    }

    private async Task FinalizeAsync(ImportJob job, bool cancelledByUser, CancellationToken cancellationToken)
    {
        // Any row still Pending was claimed by a run that died before it could record an outcome.
        // Its real result is genuinely unknown, so it is reported as such rather than guessed at --
        // the user re-uploads exactly those rows.
        var stranded = await db.ImportJobRows
            .Where(r => r.ImportJobId == job.Id && r.Status == ImportJobRowStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var row in stranded)
        {
            row.MarkFailed(
                null, "The import was interrupted before this row's outcome could be recorded; re-upload this row.");
        }

        // Committed before the counts are computed, deliberately: ApplyCountsAsync aggregates in the
        // database, so an outcome that is only sitting in the change tracker would not be counted
        // and the job would report a stranded row as neither succeeded nor failed.
        await db.SaveChangesAsync(cancellationToken);

        await ApplyCountsAsync(job, cancellationToken);

        var now = timeProvider.GetUtcNow();
        if (cancelledByUser)
        {
            job.MarkCancelled(now);
        }
        else
        {
            job.MarkCompleted(now);
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Import job {ImportJobId} finished as {Status}: {Succeeded} succeeded, {Failed} failed of {Total}.",
            job.Id, job.Status, job.SucceededRowCount, job.FailedRowCount, job.TotalRowCount);

        await NotifyAsync(job, cancellationToken);
    }

    private async Task FailAsync(ImportJob job, string reason, CancellationToken cancellationToken)
    {
        await ApplyCountsAsync(job, cancellationToken);
        job.MarkFailed(timeProvider.GetUtcNow(), reason);
        await db.SaveChangesAsync(cancellationToken);

        await NotifyAsync(job, cancellationToken);
    }

    /// <summary>
    /// <b>Decision E (NFR-4.3, "with the user notified on completion").</b> The job screen polls and
    /// is the primary answer for a user who stayed; this covers the one who did not. Unlike Phase
    /// 20e's alerts -- whose recipients are unvalidated free text, and whose egress risk drove that
    /// phase's whole permission derivation -- the address here is the initiating user's <i>own</i>
    /// registered email, looked up server-side from their id. Nothing about the recipient is
    /// caller-supplied, so this reopens none of 20e's Decision B concerns.
    ///
    /// <para>Failure to notify never fails the job: the outcome is already durably recorded and
    /// visible on the screen, and turning a successful 997-row import into an error because SMTP was
    /// down would be indefensible.</para>
    /// </summary>
    private async Task NotifyAsync(ImportJob job, CancellationToken cancellationToken)
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

            var subject = $"{job.EntityType} import {job.Status.ToString().ToLowerInvariant()} - {job.FileName}";
            var body = job.Status == ImportJobStatus.Failed
                ? $"Your {job.EntityType} import of '{job.FileName}' could not be processed.\r\n\r\n{job.FailureReason}"
                : $"Your {job.EntityType} import of '{job.FileName}' finished as {job.Status}.\r\n\r\n"
                    + $"Rows processed: {job.ProcessedRowCount} of {job.TotalRowCount}\r\n"
                    + $"Succeeded: {job.SucceededRowCount}\r\n"
                    + $"Failed: {job.FailedRowCount}\r\n\r\n"
                    + "Open the Import / Export screen to see the per-row results.";

            await emailSender.SendAsync(email, subject, body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not notify the initiator of import job {ImportJobId}.", job.Id);
        }
    }
}

/// <summary>Stops a job for a reason that is about the job, not a row -- currently only a revoked
/// permission. Caught by <see cref="ImportJobProcessor.ProcessNextAsync"/>, which marks the job
/// Failed with this message.</summary>
public sealed class ImportJobAbortedException(string message) : Exception(message);
