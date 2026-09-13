using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Imports;

/// <summary>
/// Phase 38 -- the one problem the two tree importers share, asserted once per importer because
/// they are two files and the guarantee is per file.
///
/// <para><b>The acceptance bar for this phase is that a bad file is refused with a row-level reason
/// before anything is committed</b>, not that a good file works. So the interesting tests here are
/// the negatives: a forward parent reference (which must <i>succeed</i>, because the sequencer's
/// whole job is to make ordering a non-problem) and a cycle (which must fail the entire file with
/// its rows named, importing nothing at all).</para>
/// </summary>
public class HierarchicalImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] CategoryHeaders = ["Category Name", "Parent Category"];
    private static readonly string[] GroupHeaders = ["Name", "Parent Group", "Primary Group"];

    /// <summary>
    /// The file names a child before its parent. The reference product's own template asks the user
    /// to avoid this ("Parent category should already exist or should be in the upcoming rows") and
    /// then documents that the upcoming-rows case is allowed -- so both orders must work, and the
    /// result must be a real tree rather than two roots.
    /// </summary>
    [Fact]
    public async Task A_category_whose_parent_appears_later_in_the_file_still_gets_its_parent()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await SeedAsync(db, PermissionKeys.ProductCategoryManage);

        // Deliberately upside down: the leaf first, the root last.
        host.FileReader.Returns(
            CategoryHeaders,
            ["Cashews", "Nuts"],
            ["Nuts", "Snacks Import"],
            ["Snacks Import", null]);

        var jobId = await QueueAsync(host, db, tenant, ImportEntityType.ProductCategory);
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(3, job.SucceededRowCount);
        Assert.Equal(0, job.FailedRowCount);

        var read = host.NewDbContext();
        var root = await read.ProductCategories.SingleAsync(c => c.Name == "Snacks Import");
        var middle = await read.ProductCategories.SingleAsync(c => c.Name == "Nuts");
        var leaf = await read.ProductCategories.SingleAsync(c => c.Name == "Cashews");

        // The tree, not just the rows: three parents resolved in the order the sequencer chose.
        Assert.Null(root.ParentCategoryId);
        Assert.Equal(root.Id, middle.ParentCategoryId);
        Assert.Equal(middle.Id, leaf.ParentCategoryId);
    }

    /// <summary>
    /// A cycle fails the <b>whole file</b> and imports nothing -- including the rows that are not in
    /// the cycle and would have been perfectly valid on their own.
    ///
    /// <para>That is the deliberate choice, and half-importing is the alternative it rejects: a
    /// partial tree leaves a tenant's catalogue in a state neither they nor the file describes, and
    /// the user cannot fix it by correcting the file and re-uploading, because the good rows would
    /// then collide.</para>
    /// </summary>
    [Fact]
    public async Task A_cycle_fails_the_whole_file_naming_its_rows_and_imports_nothing()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await SeedAsync(db, PermissionKeys.ProductCategoryManage);

        host.FileReader.Returns(
            CategoryHeaders,
            ["Innocent", null],
            ["Alpha", "Beta"],
            ["Beta", "Alpha"]);

        var jobId = await QueueAsync(host, db, tenant, ImportEntityType.ProductCategory);
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Contains("cycle", job.FailureReason!, StringComparison.OrdinalIgnoreCase);

        // The rows are named, because "your file has a cycle" is not actionable on a 400-row file.
        Assert.Contains("Alpha", job.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("Beta", job.FailureReason!, StringComparison.Ordinal);

        // Nothing was written -- not even the row that had nothing to do with the cycle.
        var read = host.NewDbContext();
        Assert.False(await read.ProductCategories.AnyAsync(c => c.Name == "Innocent"));
        Assert.False(await read.ProductCategories.AnyAsync(c => c.Name == "Alpha"));
        Assert.Empty(await read.ImportJobRows.Where(r => r.ImportJobId == jobId).ToListAsync());
    }

    /// <summary>A row that is its own parent is a cycle of length one, and is caught by the same
    /// mechanism rather than sorting cleanly and being rejected later by the database.</summary>
    [Fact]
    public async Task A_row_that_is_its_own_parent_is_a_cycle()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await SeedAsync(db, PermissionKeys.ProductCategoryManage);

        host.FileReader.Returns(CategoryHeaders, ["Ouroboros", "Ouroboros"]);

        var jobId = await QueueAsync(host, db, tenant, ImportEntityType.ProductCategory);
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Contains("cycle", job.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Two rows with the same name fail the file too, and for a reason worth separating from the
    /// cycle: the ordering itself becomes ambiguous, because a third row naming that parent cannot
    /// be said to mean either of them.
    /// </summary>
    [Fact]
    public async Task A_duplicate_key_fails_the_file_because_a_parent_reference_would_be_ambiguous()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await SeedAsync(db, PermissionKeys.ProductCategoryManage);

        host.FileReader.Returns(
            CategoryHeaders,
            ["Nuts", null],
            ["Nuts", null],
            ["Cashews", "Nuts"]);

        var jobId = await QueueAsync(host, db, tenant, ImportEntityType.ProductCategory);
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Contains("more than once", job.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("rows 2, 3", job.FailureReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A parent that is neither in the file nor in the tenant is <b>one row's</b> problem, not the
    /// file's. This is the line the sequencer deliberately does not cross: it only knows about edges
    /// inside the file, so an unknown name is left for the importer to reject with a message naming
    /// it, and every other row still imports.
    /// </summary>
    [Fact]
    public async Task An_unknown_parent_is_one_row_error_and_the_rest_of_the_file_imports()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await SeedAsync(db, PermissionKeys.ProductCategoryManage);

        host.FileReader.Returns(
            CategoryHeaders,
            ["Cashews", "Does Not Exist"],
            ["Almonds", null]);

        var jobId = await QueueAsync(host, db, tenant, ImportEntityType.ProductCategory);
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(1, job.SucceededRowCount);
        Assert.Equal(1, job.FailedRowCount);

        var read = host.NewDbContext();
        var failed = await read.ImportJobRows.SingleAsync(r => r.ImportJobId == jobId && r.Message != null);
        Assert.Equal("Parent Category", failed.ColumnName);
        Assert.Contains("neither in this organization nor in this file", failed.Message!, StringComparison.Ordinal);
        Assert.True(await read.ProductCategories.AnyAsync(c => c.Name == "Almonds"));
    }

    /// <summary>
    /// The same forward-reference guarantee on the second tree, plus the check that only Account
    /// Group has: a child declaring a different Primary Group from its parent is a row error rather
    /// than a group silently filed under the wrong root.
    /// </summary>
    [Fact]
    public async Task An_account_group_inherits_its_place_and_must_agree_with_its_parents_primary_group()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await SeedAsync(db, PermissionKeys.AccountGroupManage);

        host.FileReader.Returns(
            GroupHeaders,
            ["Office Costs", "Overheads", "Expense"],
            ["Overheads", null, "Expense"],
            ["Misfiled", "Overheads", "Asset"]);

        var jobId = await QueueAsync(host, db, tenant, ImportEntityType.AccountGroup);
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(2, job.SucceededRowCount);
        Assert.Equal(1, job.FailedRowCount);

        var read = host.NewDbContext();
        var parent = await read.AccountGroups.SingleAsync(g => g.Name == "Overheads");
        var child = await read.AccountGroups.SingleAsync(g => g.Name == "Office Costs");
        Assert.Equal(parent.Id, child.ParentGroupId);
        Assert.Equal(AccountRootType.Expense, child.RootType);

        var failed = await read.ImportJobRows.SingleAsync(r => r.ImportJobId == jobId && r.Message != null);
        Assert.Equal("Primary Group", failed.ColumnName);
        Assert.False(await read.AccountGroups.AnyAsync(g => g.Name == "Misfiled"));
    }

    /// <summary>Both tree types are Create-only, matching the reference product, and the refusal
    /// happens at upload rather than as one identical row error per row.</summary>
    [Theory]
    [InlineData(ImportEntityType.ProductCategory)]
    [InlineData(ImportEntityType.AccountGroup)]
    [InlineData(ImportEntityType.ProductVariant)]
    public void Update_mode_is_rejected_for_the_create_only_types(ImportEntityType entityType)
    {
        var command = new ErpApp.Application.Imports.Commands.CreateImportJob.CreateImportJobCommand(
            Guid.NewGuid(), entityType, ImportMode.UpdateExisting, "rows.xlsx", 1024, Stream.Null);

        var result = new ErpApp.Application.Imports.Commands.CreateImportJob.CreateImportJobCommandValidator()
            .Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            e => e.ErrorMessage.Contains("can only create records", StringComparison.Ordinal));
    }

    private static async Task<ImportTenant> SeedAsync(IAppDbContext db, string manageKey) =>
        await ImportTestSeed.SeedAsync(db, PermissionKeys.ImportJobManage, manageKey);

    private static Task<Guid> QueueAsync(
        ImportTestHost host, IAppDbContext db, ImportTenant tenant, ImportEntityType entityType) =>
        ImportTestSeed.QueueJobAsync(
            db, tenant, entityType, ImportMode.CreateNew, Now, host.FileStorage);

    private static async Task<ImportJob> LoadJobAsync(ImportTestHost host, Guid jobId) =>
        await host.NewDbContext().ImportJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
}
