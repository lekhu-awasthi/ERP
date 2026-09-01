using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Exports;
using ErpApp.Application.Exports.Commands.CancelExportJob;
using ErpApp.Application.Exports.Commands.CreateExportJob;
using ErpApp.Application.Exports.Queries.GetExportJobArtifact;
using ErpApp.Application.Exports.Queries.ListExportJobs;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ErpApp.Application.UnitTests.Exports;

/// <summary>
/// The export runner's behavioural suite. Every test drives <see cref="ExportJobProcessor"/> through
/// a real DI container (see <see cref="ExportTestHost"/>) with a <c>FakeTimeProvider</c> -- no
/// <c>Task.Delay</c>, no <c>Thread.Sleep</c>, no real clock.
///
/// <para><b>What the InMemory provider cannot prove, stated up front.</b> It enforces neither unique
/// indexes nor concurrency tokens, so nothing here can exercise two runners racing for one job --
/// which is fine, because unlike Phase 21a this design does not depend on winning that race: an
/// export is idempotent, so a duplicate run wastes effort and produces one extra orphaned blob,
/// never a wrong artifact. What is verified here is everything a single instance can do, plus the
/// restart path. The real .xlsx bytes are verified separately, against the real ClosedXML writer, by
/// <c>ExportWorkbookWriterTests</c> in Api.IntegrationTests; the Kestrel/Content-Disposition path is
/// manual E2E only.</para>
/// </summary>
public class ExportJobProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Writes_one_sheet_per_category_with_its_headers_and_rows()
    {
        using var host = new ExportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ExportTestSeed.SeedAsync(db);
        var jobId = await ExportTestSeed.QueueJobAsync(db, tenant, Now);

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var workbook = host.WorkbookWriter.LastWorkbook;
        Assert.NotNull(workbook);

        // Summary first, then FR-2.8's five categories in the order the requirement names them.
        Assert.Equal(
            ["Summary", "Products", "Contacts", "Chart of Accounts", "Ledger Transactions", "Stock Movements"],
            workbook.Sheets.Select(s => s.Name));

        var products = host.WorkbookWriter.Sheet("Products");
        Assert.Equal("Product Code", products.Headers[0]);
        Assert.Equal("Product Name", products.Headers[1]);
        var productRow = Assert.Single(products.Rows);
        Assert.Equal(tenant.ProductCode, productRow[0]);
        Assert.Equal("Salted Cashew A", productRow[1]);
        Assert.Equal("Snacks A", productRow[3]);
        Assert.Equal("Box A", productRow[4]);

        var contacts = host.WorkbookWriter.Sheet("Contacts");
        var contactRow = Assert.Single(contacts.Rows);
        Assert.Equal(tenant.ContactCode, contactRow[0]);
        Assert.Equal("Customer", contactRow[2]);
        Assert.Equal("304567847", contactRow[4]);

        var accounts = host.WorkbookWriter.Sheet("Chart of Accounts");
        Assert.Equal(2, accounts.Rows.Count);
        Assert.Contains(accounts.Rows, r => Equals(r[0], tenant.AccountCode));

        // Both GL lines of the seeded balanced entry, joined to their account names.
        var ledger = host.WorkbookWriter.Sheet("Ledger Transactions");
        Assert.Equal(2, ledger.Rows.Count);
        Assert.Contains(ledger.Rows, r => Equals(r[4], "Cash A") && Equals(r[5], 1000m));
        Assert.Contains(ledger.Rows, r => Equals(r[4], "Sales A") && Equals(r[6], 1000m));

        var stock = host.WorkbookWriter.Sheet("Stock Movements");
        var stockRow = Assert.Single(stock.Rows);
        Assert.Equal(new DateOnly(2026, 8, 20), stockRow[0]);
        Assert.Equal(tenant.ProductCode, stockRow[1]);
        Assert.Equal("Main Store A", stockRow[3]);
        Assert.Equal("In", stockRow[4]);
        Assert.Equal(800m, stockRow[7]);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ExportJobStatus.Completed, job.Status);
        Assert.Equal(5, job.ProcessedCategoryCount);
        Assert.Equal(7, job.TotalRowCount);
        Assert.Null(job.TruncationNotice);
        Assert.True(job.HasArtifact);
        Assert.True(host.FileStorage.Contains(job.StorageKey!));
        Assert.True(job.FileSizeBytes > 0);
        Assert.EndsWith(".xlsx", job.FileName, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The headline test of the phase.</b> There is no EF global query filter in this codebase --
    /// every filter here is hand-written, across more tables at once than any earlier feature -- and
    /// <c>GlLine</c> in particular has no OrganizationId of its own, so its isolation depends
    /// entirely on a join. Asserting that A's rows are present would not catch any of that; this
    /// asserts that B's marker appears in <i>no cell of any sheet</i>.
    /// </summary>
    [Fact]
    public async Task Exports_only_the_requesting_tenants_rows()
    {
        using var host = new ExportTestHost(Now);
        var db = host.NewDbContext();
        var tenantA = await ExportTestSeed.SeedAsync(db, "A");
        await ExportTestSeed.SeedAsync(db, "B");
        var jobId = await ExportTestSeed.QueueJobAsync(db, tenantA, Now);

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var workbook = host.WorkbookWriter.LastWorkbook!;
        var everyCell = workbook.Sheets
            .SelectMany(s => s.Rows)
            .SelectMany(r => r)
            .Select(c => c?.ToString() ?? string.Empty)
            .ToList();

        Assert.Contains(everyCell, c => c.Contains("Salted Cashew A", StringComparison.Ordinal));
        Assert.DoesNotContain(everyCell, c => c.Contains(" B", StringComparison.Ordinal));
        Assert.DoesNotContain(everyCell, c => c.Contains("-B-", StringComparison.Ordinal));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(7, job.TotalRowCount);
    }

    [Fact]
    public async Task An_empty_tenant_exports_successfully()
    {
        using var host = new ExportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ExportTestSeed.SeedEmptyAsync(db);
        var jobId = await ExportTestSeed.QueueJobAsync(db, tenant, Now);

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ExportJobStatus.Completed, job.Status);
        Assert.Equal(0, job.TotalRowCount);
        Assert.True(job.HasArtifact);

        // Every sheet still exists, with its headers -- an empty tenant gets a usable template of a
        // workbook, not a file that is missing sheets or a job that failed.
        var workbook = host.WorkbookWriter.LastWorkbook!;
        Assert.Equal(6, workbook.Sheets.Count);
        Assert.All(workbook.Sheets, s => Assert.NotEmpty(s.Headers));
        Assert.All(
            workbook.Sheets.Where(s => s.Name != "Summary"),
            s => Assert.Empty(s.Rows));
    }

    [Fact]
    public async Task A_category_past_the_row_cap_is_truncated_and_disclosed()
    {
        using var host = new ExportTestHost(Now, ReplaceReadersWith(new StubCategoryReader(
            ExportCategory.Products, "Products", ["Product Code"], rowCount: 2, totalRowCount: 41_233)));

        var db = host.NewDbContext();
        var tenant = await ExportTestSeed.SeedAsync(db);
        var jobId = await ExportTestSeed.QueueJobAsync(db, tenant, Now);

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);

        // Truncation is not a failure: the file is complete and downloadable.
        Assert.Equal(ExportJobStatus.Completed, job.Status);
        Assert.True(job.HasArtifact);
        Assert.Equal("Products (2 of 41,233 rows)", job.TruncationNotice);

        // Disclosed in the workbook itself, not only on the job row.
        var summary = host.WorkbookWriter.Sheet("Summary");
        Assert.Contains(summary.Preamble, line => line.StartsWith("TRUNCATED:", StringComparison.Ordinal));

        var productsRow = Assert.Single(summary.Rows, r => Equals(r[0], "Products"));
        Assert.Equal(2, productsRow[1]);
        Assert.Equal(41_233, productsRow[2]);
        Assert.Equal("Yes", productsRow[3]);

        // Only the category that overflowed is flagged.
        Assert.All(summary.Rows.Where(r => !Equals(r[0], "Products")), r => Assert.Equal("No", r[3]));

        // ...and in the completion email.
        var mail = Assert.Single(host.EmailSender.SentEmails);
        Assert.Contains("41,233", mail.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_summary_sheet_says_the_file_is_not_a_restorable_backup()
    {
        using var host = new ExportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ExportTestSeed.SeedAsync(db);
        await ExportTestSeed.QueueJobAsync(db, tenant, Now);

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var summary = host.WorkbookWriter.Sheet("Summary");
        Assert.Contains(summary.Preamble, line => line.Contains("not a restorable backup", StringComparison.Ordinal));
        Assert.Contains(summary.Preamble, line => line.Contains(tenant.OrganizationName, StringComparison.Ordinal));
    }

    /// <summary>Nepal wall clock, not UTC. 19:00 UTC on 1 September is already 00:45 on 2 September
    /// in Kathmandu, so a UTC-derived stamp would put yesterday's date on the file and on every
    /// timestamp column. See CLAUDE.md's NepalTime gotcha.</summary>
    [Fact]
    public async Task Stamps_the_file_name_on_the_Nepal_day_not_the_UTC_day()
    {
        var lateEvening = new DateTimeOffset(2026, 9, 1, 19, 0, 0, TimeSpan.Zero);
        using var host = new ExportTestHost(lateEvening);
        var db = host.NewDbContext();
        var tenant = await ExportTestSeed.SeedAsync(db);
        var jobId = await ExportTestSeed.QueueJobAsync(db, tenant, lateEvening);

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal("DataExport_Acme-Traders-A_2026-09-02_0045.xlsx", job.FileName);
    }

    [Fact]
    public async Task A_writer_failure_leaves_no_downloadable_artifact()
    {
        using var host = new ExportTestHost(Now);
        host.WorkbookWriter.OnWrite = _ => new IOException("disk full");

        var db = host.NewDbContext();
        var tenant = await ExportTestSeed.SeedAsync(db);
        var jobId = await ExportTestSeed.QueueJobAsync(db, tenant, Now);

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ExportJobStatus.Failed, job.Status);
        Assert.Contains("disk full", job.FailureReason, StringComparison.Ordinal);
        Assert.Null(job.StorageKey);
        Assert.False(job.HasArtifact);

        // The download endpoint's query refuses it too, rather than 500ing on a null key.
        host.CurrentUser.UserId = tenant.AdminUserId;
        await Assert.ThrowsAsync<NotFoundException>(() =>
            host.Send(new GetExportJobArtifactQuery(tenant.OrganizationId, jobId)));
    }

    /// <summary>An export is idempotent, which is the whole reason it needs no per-row ledger: a run
    /// whose process died is simply rebuilt from scratch by the next runner.</summary>
    [Fact]
    public async Task An_abandoned_running_job_is_reclaimed_and_regenerated()
    {
        using var host = new ExportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ExportTestSeed.SeedAsync(db);
        var jobId = await ExportTestSeed.QueueJobAsync(db, tenant, Now);

        // Simulate a process that claimed the job and then died: Running, heartbeat older than the
        // two-minute lease, no artifact.
        var stale = await db.ExportJobs.SingleAsync(j => j.Id == jobId);
        stale.Claim(Now - TimeSpan.FromMinutes(10));
        await db.SaveChangesAsync();

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ExportJobStatus.Completed, job.Status);
        Assert.Equal(7, job.TotalRowCount);
        Assert.Equal(1, host.WorkbookWriter.WriteCount);
        Assert.True(host.FileStorage.Contains(job.StorageKey!));
    }

    [Fact]
    public async Task Cancelling_a_running_export_leaves_no_artifact()
    {
        // The stub reads the first category and, while doing so, raises the user's cancel flag --
        // which is the only way to land a cancel exactly on a category boundary in a test.
        ExportTestHost? host = null;
        var cancelRequested = false;

        host = new ExportTestHost(Now, ReplaceReadersWith(
            new StubCategoryReader(ExportCategory.Products, "Products", ["Product Code"], 1, 1, onRead: async () =>
            {
                if (cancelRequested)
                {
                    return;
                }

                cancelRequested = true;
                var db = host!.NewDbContext();
                var job = await db.ExportJobs.FirstAsync();
                job.RequestCancellation();
                await db.SaveChangesAsync();
            }),
            new StubCategoryReader(ExportCategory.Contacts, "Contacts", ["Code"], 1, 1)));

        using (host)
        {
            var seedDb = host.NewDbContext();
            var tenant = await ExportTestSeed.SeedAsync(seedDb);
            var jobId = await ExportTestSeed.QueueJobAsync(seedDb, tenant, Now);

            Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

            var job = await LoadJobAsync(host, jobId);
            Assert.Equal(ExportJobStatus.Cancelled, job.Status);
            Assert.Null(job.StorageKey);
            Assert.False(job.HasArtifact);
            Assert.Equal(0, host.WorkbookWriter.WriteCount);
        }
    }

    /// <summary>Decision E. The file is genuinely gone from storage, the row survives to say so, and
    /// the download path reports "expired" rather than offering a dead link.</summary>
    [Fact]
    public async Task Retention_deletes_the_artifact_and_the_row_says_so()
    {
        using var host = new ExportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ExportTestSeed.SeedAsync(db);
        var jobId = await ExportTestSeed.QueueJobAsync(db, tenant, Now);

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);
        var storageKey = (await LoadJobAsync(host, jobId)).StorageKey!;
        Assert.True(host.FileStorage.Contains(storageKey));

        // One second before expiry: nothing happens.
        host.Clock.SetUtcNow(Now + TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        await host.NewProcessor().SweepAsync(CancellationToken.None);
        Assert.True(host.FileStorage.Contains(storageKey));

        host.Clock.SetUtcNow(Now + TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        await host.NewProcessor().SweepAsync(CancellationToken.None);

        Assert.False(host.FileStorage.Contains(storageKey));

        var job = await LoadJobAsync(host, jobId);
        Assert.NotNull(job.ArtifactPurgedAt);
        Assert.Null(job.StorageKey);
        Assert.False(job.HasArtifact);
        Assert.Equal(ExportJobStatus.Completed, job.Status);
        Assert.NotNull(job.FileName);

        host.CurrentUser.UserId = tenant.AdminUserId;
        var error = await Assert.ThrowsAsync<NotFoundException>(() =>
            host.Send(new GetExportJobArtifactQuery(tenant.OrganizationId, jobId)));
        Assert.Contains("expired", error.Message, StringComparison.OrdinalIgnoreCase);

        // Idempotent: a second sweep finds nothing left to do rather than re-deleting.
        await host.NewProcessor().SweepAsync(CancellationToken.None);
        Assert.Equal(job.ArtifactPurgedAt, (await LoadJobAsync(host, jobId)).ArtifactPurgedAt);
    }

    [Fact]
    public async Task Notifies_the_initiator_at_their_own_registered_address()
    {
        using var host = new ExportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ExportTestSeed.SeedAsync(db);
        await ExportTestSeed.QueueJobAsync(db, tenant, Now);

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var expected = await host.NewDbContext().Users
            .Where(u => u.Id == tenant.AdminUserId)
            .Select(u => u.Email)
            .SingleAsync();

        var mail = Assert.Single(host.EmailSender.SentEmails);
        Assert.Equal(expected, mail.To);
        Assert.Contains("not a restorable backup", mail.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Returns_false_when_there_is_nothing_queued()
    {
        using var host = new ExportTestHost(Now);
        Assert.False(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));
    }

    private static async Task<ExportJob> LoadJobAsync(ExportTestHost host, Guid jobId) =>
        await host.NewDbContext().ExportJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);

    /// <summary>
    /// Swaps in the given stubs and fills every remaining <see cref="ExportCategory"/> with an empty
    /// one. The fill is not padding: <c>ExportJobProcessor.ResolveReaders</c> deliberately throws
    /// when a category has no registration, so that a forgotten DI line fails loudly instead of
    /// quietly producing a workbook missing a sheet FR-2.8 names. A test that registered only the
    /// stub it cares about would trip that guard rather than exercise what it meant to.
    /// </summary>
    private static Action<IServiceCollection> ReplaceReadersWith(params StubCategoryReader[] readers) =>
        services =>
        {
            services.RemoveAll<IExportCategoryReader>();

            var provided = readers.Select(r => r.Category).ToHashSet();
            IExportCategoryReader[] all =
            [
                .. readers,
                .. Enum.GetValues<ExportCategory>()
                    .Where(c => !provided.Contains(c))
                    .Select(c => new StubCategoryReader(c, c.ToString(), ["Value"], 0, 0)),
            ];

            foreach (var reader in all)
            {
                var captured = reader;
                services.AddScoped(_ => captured);
            }
        };
}

/// <summary>A reader that reports whatever counts a test needs -- the only way to reach the 25,000
/// -row cap and the cancel-at-a-category-boundary path without seeding 25,000 rows.</summary>
internal sealed class StubCategoryReader(
    ExportCategory category,
    string sheetName,
    IReadOnlyList<string> headers,
    int rowCount,
    int totalRowCount,
    Func<Task>? onRead = null) : IExportCategoryReader
{
    public ExportCategory Category => category;

    public string SheetName => sheetName;

    public IReadOnlyList<string> Headers => headers;

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, CancellationToken cancellationToken)
    {
        if (onRead is not null)
        {
            await onRead();
        }

        var rows = Enumerable.Range(0, rowCount)
            .Select(i => new object?[] { $"row-{i}" })
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }
}
