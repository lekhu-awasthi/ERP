using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Imports.Commands.CancelImportJob;
using ErpApp.Application.Imports.Commands.ConfirmImportJob;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Imports;

/// <summary>
/// Phase 38's pre-commit review: the user sees what will happen before it happens.
///
/// <para><b>The property worth testing is the one that makes this a review rather than a preview:
/// the validate pass writes nothing.</b> Everything else -- the counts, the wording, the button --
/// is presentation. So every test here asserts the tenant's own tables are untouched at the moment
/// the job reaches PendingConfirmation, and only then asserts what the user is shown.</para>
/// </summary>
public class ImportReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] CategoryHeaders = ["Category Name", "Parent Category"];

    [Fact]
    public async Task The_dry_run_writes_nothing_and_stops_for_a_person()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(
            db, PermissionKeys.ImportJobManage, PermissionKeys.ProductManage);

        host.FileReader.Returns(
            ImportTestSeed.ProductHeaders,
            ImportTestSeed.ProductRow("Reviewed Cashew"),
            ImportTestSeed.ProductRow("Reviewed Almond"));

        var jobId = await QueueReviewedAsync(host, db, tenant, ImportEntityType.Product);

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.PendingConfirmation, job.Status);
        Assert.Equal(2, job.TotalRowCount);
        Assert.Equal(0, job.FailedRowCount);
        Assert.Null(job.ReviewConfirmedAt);

        // Nothing was created. This is the whole claim.
        Assert.Empty(await host.NewDbContext().Products.Where(p => p.Name.StartsWith("Reviewed")).ToListAsync());

        // And the runner does not pick it up again: a job waiting for a person is not a stalled job,
        // so no lease expires on it.
        Assert.False(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Confirming_applies_exactly_the_rows_that_validated()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(
            db, PermissionKeys.ImportJobManage, PermissionKeys.ProductManage);

        host.FileReader.Returns(
            ImportTestSeed.ProductHeaders,
            ImportTestSeed.ProductRow("Good One"),
            // "Nowhere" is not a category in this tenant, so PlanAsync rejects this row before any
            // command exists -- the dry run's own finding, not the handler's.
            ImportTestSeed.ProductRow("Bad One", category: "Nowhere"),
            ImportTestSeed.ProductRow("Good Two"));

        var jobId = await QueueReviewedAsync(host, db, tenant, ImportEntityType.Product);
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var reviewed = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.PendingConfirmation, reviewed.Status);
        Assert.Equal(1, reviewed.FailedRowCount);

        // The finding is a row error with its column named, which is what the review screen renders
        // -- the reference product's own "Row: {LineNo} {Header} {Message}".
        var finding = await host.NewDbContext().ImportJobRows
            .SingleAsync(r => r.ImportJobId == jobId && r.Status == ImportJobRowStatus.Failed);
        // Row 3: the sheet's own 1-based number with the header included, so the first data row is 2
        // and this is the second of them -- which is what the user sees in Excel.
        Assert.Equal(3, finding.RowNumber);
        Assert.Equal("Category", finding.ColumnName);

        await ConfirmAsync(host, tenant, jobId);

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var applied = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, applied.Status);
        Assert.Equal(2, applied.SucceededRowCount);

        // The rejected row stayed rejected and was never retried: its ledger row was already
        // terminal, which is the same mechanism that makes a crashed import resumable.
        Assert.Equal(1, applied.FailedRowCount);

        var products = await host.NewDbContext().Products
            .Where(p => p.Name.EndsWith("One") || p.Name.EndsWith("Two"))
            .Select(p => p.Name)
            .ToListAsync();

        Assert.Equal(2, products.Count);
        Assert.DoesNotContain("Bad One", products);
    }

    /// <summary>
    /// A row whose parent is created by a <i>later</i> row of the same file must validate clean.
    ///
    /// <para>This is the case the dry run could most easily get wrong, and the one
    /// <c>ImportRowContext.PendingKeys</c> exists for: nothing has been written during validation, so
    /// the ordinary name lookup finds no parent, and without the pending-key set the review screen
    /// would report a perfectly correct file as full of errors -- then apply it successfully.</para>
    /// </summary>
    [Fact]
    public async Task A_forward_parent_reference_validates_clean_rather_than_reading_as_an_error()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(
            db, PermissionKeys.ImportJobManage, PermissionKeys.ProductCategoryManage);

        host.FileReader.Returns(
            CategoryHeaders,
            ["Child", "Parent"],
            ["Parent", null]);

        var jobId = await QueueReviewedAsync(host, db, tenant, ImportEntityType.ProductCategory);
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.PendingConfirmation, job.Status);
        Assert.Equal(0, job.FailedRowCount);
        Assert.Empty(await host.NewDbContext().ProductCategories.Where(c => c.Name == "Parent").ToListAsync());

        await ConfirmAsync(host, tenant, jobId);
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var read = host.NewDbContext();
        var parent = await read.ProductCategories.SingleAsync(c => c.Name == "Parent");
        var child = await read.ProductCategories.SingleAsync(c => c.Name == "Child");
        Assert.Equal(parent.Id, child.ParentCategoryId);
    }

    /// <summary>
    /// Discarding a reviewed import is the ordinary cancel -- nothing was written, so there is
    /// nothing to undo and no separate Discard command earns its place.
    /// </summary>
    [Fact]
    public async Task Cancelling_from_review_retires_the_job_immediately_and_applies_nothing()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(
            db, PermissionKeys.ImportJobManage, PermissionKeys.ProductManage);

        host.FileReader.Returns(ImportTestSeed.ProductHeaders, ImportTestSeed.ProductRow("Discarded"));

        var jobId = await QueueReviewedAsync(host, db, tenant, ImportEntityType.Product);
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        await new CancelImportJobCommandHandler(host.NewDbContext(), host.Clock)
            .Handle(new CancelImportJobCommand(tenant.OrganizationId, jobId), CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Cancelled, job.Status);

        Assert.False(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));
        Assert.Empty(await host.NewDbContext().Products.Where(p => p.Name == "Discarded").ToListAsync());
    }

    /// <summary>Confirming anything that is not waiting for confirmation is a 409 naming the status
    /// it is actually in -- a stale screen, a race with the runner, and a no-op that reads like an
    /// action are the same mistake.</summary>
    [Fact]
    public async Task Confirming_a_job_that_is_not_awaiting_confirmation_is_a_conflict()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(
            db, PermissionKeys.ImportJobManage, PermissionKeys.ProductManage);

        host.FileReader.Returns(ImportTestSeed.ProductHeaders, ImportTestSeed.ProductRow("Straight Through"));

        // Queued with no review at all -- the phase-21a behaviour, still available.
        var jobId = await ImportTestSeed.QueueJobAsync(
            db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        var conflict = await Assert.ThrowsAsync<ConflictException>(
            () => ConfirmAsync(host, tenant, jobId));

        Assert.Contains("Queued", conflict.Message, StringComparison.Ordinal);

        // And the unreviewed job still applies in one pass, unchanged.
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));
        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(1, job.SucceededRowCount);
    }

    /// <summary>
    /// A file that cannot be processed at all fails during the dry run rather than waiting for a
    /// confirmation it will never honour -- the distinction <c>ImportJobStatus</c> draws between a
    /// whole-file problem and a row's own fault, now reachable one pass earlier.
    /// </summary>
    [Fact]
    public async Task A_whole_file_problem_fails_during_the_review_pass()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(
            db, PermissionKeys.ImportJobManage, PermissionKeys.ProductCategoryManage);

        host.FileReader.Returns(CategoryHeaders, ["A", "B"], ["B", "A"]);

        var jobId = await QueueReviewedAsync(host, db, tenant, ImportEntityType.ProductCategory);
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Contains("cycle", job.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Drives the real handler rather than a MediatR pipeline: the command's permission key is
    /// declarative (<c>IRequirePermission</c>) and proved by AuthorizationBehavior's own suite plus
    /// the phase's manual E2E, so building a pipeline host here would test the behavior a second
    /// time and this phase's own logic not at all.
    /// </summary>
    private static Task ConfirmAsync(ImportTestHost host, ImportTenant tenant, Guid jobId) =>
        new ConfirmImportJobCommandHandler(host.NewDbContext(), host.Clock)
            .Handle(new ConfirmImportJobCommand(tenant.OrganizationId, jobId), CancellationToken.None);

    private static Task<Guid> QueueReviewedAsync(
        ImportTestHost host, IAppDbContext db, ImportTenant tenant, ImportEntityType entityType) =>
        ImportTestSeed.QueueJobAsync(
            db, tenant, entityType, ImportMode.CreateNew, Now, host.FileStorage, reviewBeforeApply: true);

    private static async Task<ImportJob> LoadJobAsync(ImportTestHost host, Guid jobId) =>
        await host.NewDbContext().ImportJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
}
