using ErpApp.Application.Common.Security;
using ErpApp.Application.Imports;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Imports;

/// <summary>
/// The import runner's behavioural suite. Every test drives <see cref="ImportJobProcessor"/> through
/// a real DI container (see <see cref="ImportTestHost"/>) with a <c>FakeTimeProvider</c> -- no
/// <c>Task.Delay</c>, no <c>Thread.Sleep</c>, no real clock, which is exactly why the timer lives in
/// the hosted service and every decision lives here.
///
/// <para><b>What the InMemory provider cannot prove, stated up front.</b> It does not enforce unique
/// indexes and does not generate rowversions, so two of this design's second-line defences are
/// unreachable from here: the (ImportJobId, RowNumber) unique index that stops two runners
/// processing one row, and the ImportJob rowversion that stops two runners claiming one job. Both
/// are asserted in the migration and verified against real SQL Server during manual E2E. The
/// <i>first</i>-line defences -- the already-claimed row set and the claim-before-act ordering --
/// are what protect the single-instance and restart cases, and those are covered in full below.
/// This is the same split Phase 20e recorded for AlertSendLog.</para>
/// </summary>
public class ImportJobProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Creates_a_product_per_row_and_completes()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        var jobId = await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        host.FileReader.Returns(
            ImportTestSeed.ProductHeaders,
            ImportTestSeed.ProductRow("Extra Energy Biscuit"),
            ImportTestSeed.ProductRow("Salted Cashew"));

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(2, job.TotalRowCount);
        Assert.Equal(2, job.SucceededRowCount);
        Assert.Equal(0, job.FailedRowCount);

        var products = await host.NewDbContext().Products
            .Where(p => p.OrganizationId == tenant.OrganizationId)
            .OrderBy(p => p.Name)
            .ToListAsync();

        Assert.Equal(["Extra Energy Biscuit", "Salted Cashew"], products.Select(p => p.Name));
        Assert.All(products, p => Assert.Equal(tenant.CategoryId, p.CategoryId));
        Assert.All(products, p => Assert.Equal(VatRate.ThirteenPercentVat, p.VatRate));
    }

    /// <summary>
    /// FR-2.9's central requirement, and the whole of Decision C's status model: a file with bad rows
    /// among good ones is a <b>successful</b> job that created the good ones and reported the bad
    /// ones by spreadsheet row number and column.
    /// </summary>
    [Fact]
    public async Task Partial_success_completes_the_job_and_reports_each_bad_row_by_number_and_column()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        var jobId = await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        host.FileReader.Returns(
            ImportTestSeed.ProductHeaders,
            ImportTestSeed.ProductRow("Good One"),
            ImportTestSeed.ProductRow("Bad Category", category: "Nonexistent Category"),
            ImportTestSeed.ProductRow("Good Two"),
            ImportTestSeed.ProductRow(string.Empty));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(4, job.TotalRowCount);
        Assert.Equal(2, job.SucceededRowCount);
        Assert.Equal(2, job.FailedRowCount);

        var failures = await LoadRowsAsync(host, jobId, ImportJobRowStatus.Failed);

        // Row 3 is the second data row: header is row 1, so the numbers point at what the user sees.
        var badCategory = failures.Single(r => r.RowNumber == 3);
        Assert.Equal("Category", badCategory.ColumnName);
        Assert.Contains("Nonexistent Category", badCategory.Message!, StringComparison.Ordinal);

        var blankName = failures.Single(r => r.RowNumber == 5);
        Assert.Equal("Product Name", blankName.ColumnName);

        Assert.Equal(2, await host.NewDbContext().Products.CountAsync(p => p.OrganizationId == tenant.OrganizationId));
    }

    /// <summary>A header mismatch is one mistake, not N: it fails the job before any row is touched,
    /// naming the columns, rather than emitting the same message a thousand times.</summary>
    [Fact]
    public async Task A_file_with_the_wrong_columns_fails_the_job_naming_them_and_touches_no_row()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        var jobId = await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        host.FileReader.Returns(["Product Code", "Widget Name", "Colour"], ["", "Anything", "Red"]);

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Contains("Product Name", job.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("Category", job.FailureReason!, StringComparison.Ordinal);
        Assert.Empty(await LoadRowsAsync(host, jobId));
        Assert.Empty(await host.NewDbContext().Products.ToListAsync());
    }

    [Fact]
    public async Task An_empty_file_fails_the_job_with_a_readable_message()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        var jobId = await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        host.FileReader.Returns(ImportTestSeed.ProductHeaders);

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Contains("no data rows", job.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An unreadable upload must surface the reader's own message, not a
    /// NullReferenceException or a bare 500.</summary>
    [Fact]
    public async Task An_unreadable_file_fails_the_job_with_the_readers_message()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        var jobId = await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        host.FileReader.Throws("The file could not be opened as an .xlsx workbook: bad zip header.");

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Contains("bad zip header", job.FailureReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file that repeats a key inside itself. Nothing stops two rows creating two products with the
    /// same name -- Product has no name uniqueness rule -- so this asserts the honest behaviour
    /// (both rows succeed, two records exist) rather than pretending to a constraint the domain does
    /// not have. The update-mode counterpart below is where a repeated key actually matters.
    /// </summary>
    [Fact]
    public async Task A_file_that_repeats_a_name_creates_both_rows_because_product_names_are_not_unique()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        var jobId = await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        host.FileReader.Returns(
            ImportTestSeed.ProductHeaders,
            ImportTestSeed.ProductRow("Duplicate Widget"),
            ImportTestSeed.ProductRow("Duplicate Widget"));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(2, job.SucceededRowCount);

        var codes = await host.NewDbContext().Products
            .Where(p => p.Name == "Duplicate Widget")
            .Select(p => p.Code)
            .ToListAsync();

        Assert.Equal(2, codes.Distinct().Count());
    }

    [Fact]
    public async Task Update_mode_updates_the_matched_product_and_creates_nothing()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);

        var createJobId = await ImportTestSeed.QueueJobAsync(
            db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);
        host.FileReader.Returns(ImportTestSeed.ProductHeaders, ImportTestSeed.ProductRow("Original Name"));
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var created = await host.NewDbContext().Products.SingleAsync();

        var updateJobId = await ImportTestSeed.QueueJobAsync(
            host.NewDbContext(), tenant, ImportEntityType.Product, ImportMode.UpdateExisting, Now, host.FileStorage);
        host.FileReader.Returns(
            ImportTestSeed.ProductHeaders,
            ImportTestSeed.ProductRow("Renamed", code: created.Code, sellingPrice: "999"));
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        Assert.Equal(ImportJobStatus.Completed, (await LoadJobAsync(host, createJobId)).Status);
        Assert.Equal(ImportJobStatus.Completed, (await LoadJobAsync(host, updateJobId)).Status);

        var products = await host.NewDbContext().Products.ToListAsync();
        var only = Assert.Single(products);
        Assert.Equal("Renamed", only.Name);
        Assert.Equal(999m, only.SellingPrice);
        Assert.Equal(created.Code, only.Code);
    }

    /// <summary>The other half of "create and update modes differ": update mode must not quietly
    /// create a record for a code it cannot find.</summary>
    [Fact]
    public async Task Update_mode_does_not_create_when_the_code_matches_nothing()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        var jobId = await ImportTestSeed.QueueJobAsync(
            db, tenant, ImportEntityType.Product, ImportMode.UpdateExisting, Now, host.FileStorage);

        host.FileReader.Returns(
            ImportTestSeed.ProductHeaders, ImportTestSeed.ProductRow("Ghost", code: "PRODUCT-9999"));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(1, job.FailedRowCount);

        var failure = Assert.Single(await LoadRowsAsync(host, jobId, ImportJobRowStatus.Failed));
        Assert.Equal("Product Code", failure.ColumnName);
        Assert.Contains("PRODUCT-9999", failure.Message!, StringComparison.Ordinal);
        Assert.Empty(await host.NewDbContext().Products.ToListAsync());
    }

    [Fact]
    public async Task Update_mode_rejects_a_row_that_would_change_an_immutable_product_type()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);

        await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);
        host.FileReader.Returns(ImportTestSeed.ProductHeaders, ImportTestSeed.ProductRow("Goods Item"));
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);
        var created = await host.NewDbContext().Products.SingleAsync();

        var updateJobId = await ImportTestSeed.QueueJobAsync(
            host.NewDbContext(), tenant, ImportEntityType.Product, ImportMode.UpdateExisting, Now, host.FileStorage);
        host.FileReader.Returns(
            ImportTestSeed.ProductHeaders,
            ImportTestSeed.ProductRow("Goods Item", code: created.Code, type: "Service"));
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var failure = Assert.Single(await LoadRowsAsync(host, updateJobId, ImportJobRowStatus.Failed));
        Assert.Equal("Product Type", failure.ColumnName);
        Assert.Equal(ProductType.Goods, (await host.NewDbContext().Products.SingleAsync()).Type);
    }

    /// <summary>
    /// Tenant isolation. Organization B's supplier has the code organization A's file names, and A's
    /// update-mode import must not see it -- every lookup in every importer filters by
    /// OrganizationId, which is the only thing standing between two tenants in this codebase (there
    /// is no EF global query filter).
    /// </summary>
    [Fact]
    public async Task An_import_for_one_tenant_cannot_match_or_update_another_tenants_record()
    {
        using var host = new ImportTestHost(Now);
        var seedDb = host.NewDbContext();
        var tenantA = await ImportTestSeed.SeedAsync(seedDb);
        var tenantB = await ImportTestSeed.SeedAsync(host.NewDbContext());

        // Give tenant B a supplier, then have tenant A try to update it by code.
        await ImportTestSeed.QueueJobAsync(
            host.NewDbContext(), tenantB, ImportEntityType.Supplier, ImportMode.CreateNew, Now, host.FileStorage);
        host.FileReader.Returns(ImportTestSeed.SupplierHeaders, ImportTestSeed.SupplierRow("Tenant B Supplier"));
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var tenantBSupplier = await host.NewDbContext().Contacts
            .SingleAsync(c => c.OrganizationId == tenantB.OrganizationId);

        var jobId = await ImportTestSeed.QueueJobAsync(
            host.NewDbContext(), tenantA, ImportEntityType.Supplier, ImportMode.UpdateExisting, Now, host.FileStorage);
        host.FileReader.Returns(
            ImportTestSeed.SupplierHeaders,
            ImportTestSeed.SupplierRow("Hijacked", code: tenantBSupplier.Code));
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var failure = Assert.Single(await LoadRowsAsync(host, jobId, ImportJobRowStatus.Failed));
        Assert.Equal("Code", failure.ColumnName);

        var unchanged = await host.NewDbContext().Contacts.SingleAsync(c => c.Id == tenantBSupplier.Id);
        Assert.Equal("Tenant B Supplier", unchanged.Name);
        Assert.Equal(tenantB.OrganizationId, unchanged.OrganizationId);
    }

    /// <summary>Customer and Supplier are one aggregate with a discriminator, so the two upload types
    /// must not be interchangeable: a Supplier import cannot reach a Customer by code.</summary>
    [Fact]
    public async Task A_supplier_import_cannot_update_a_customer()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);

        await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Customer, ImportMode.CreateNew, Now, host.FileStorage);
        host.FileReader.Returns(
            ["Code", "Customer Name", "Contact Group", "Phone No", "Email", "Address", "PAN", "Opening Balance"],
            ImportTestSeed.SupplierRow("A Customer"));
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var customer = await host.NewDbContext().Contacts.SingleAsync();
        Assert.Equal(ContactType.Customer, customer.Type);

        var jobId = await ImportTestSeed.QueueJobAsync(
            host.NewDbContext(), tenant, ImportEntityType.Supplier, ImportMode.UpdateExisting, Now, host.FileStorage);
        host.FileReader.Returns(
            ImportTestSeed.SupplierHeaders, ImportTestSeed.SupplierRow("Now A Supplier", code: customer.Code));
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var failure = Assert.Single(await LoadRowsAsync(host, jobId, ImportJobRowStatus.Failed));
        Assert.Contains("Customer", failure.Message!, StringComparison.Ordinal);
        Assert.Equal("A Customer", (await host.NewDbContext().Contacts.SingleAsync()).Name);
    }

    /// <summary>
    /// <b>Decision C's crash story, and the most important test here.</b> A run whose process died
    /// after committing its row claims -- but before finalising the job -- leaves the job Running
    /// with a heartbeat that goes stale. A brand-new processor over the same database (a restart)
    /// re-claims and finishes it, and must not create a single product a second time.
    /// </summary>
    [Fact]
    public async Task A_resumed_job_creates_no_duplicates_for_rows_that_were_already_claimed()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        var jobId = await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        host.FileReader.Returns(
            ImportTestSeed.ProductHeaders,
            ImportTestSeed.ProductRow("Row Two"),
            ImportTestSeed.ProductRow("Row Three"),
            ImportTestSeed.ProductRow("Row Four"));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);
        Assert.Equal(3, await host.NewDbContext().Products.CountAsync());

        // The crash: the process died before it could finalise, so the job is back to Running with
        // all three row claims committed and its heartbeat frozen at the moment it stopped.
        var crashDb = host.NewDbContext();
        (await crashDb.ImportJobs.SingleAsync(j => j.Id == jobId)).Claim(Now);
        await crashDb.SaveChangesAsync();

        // Past the lease, so the abandoned job is re-claimable. The new processor shares nothing
        // with the first except the database -- the committed claims are the only thing stopping it
        // importing the whole file again.
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(3, job.SucceededRowCount);

        var names = await host.NewDbContext().Products.Select(p => p.Name).OrderBy(n => n).ToListAsync();
        Assert.Equal(["Row Four", "Row Three", "Row Two"], names);
    }

    /// <summary>The other half of the crash story: a resumed run does not merely skip, it <b>finishes
    /// the rows that were never claimed</b> -- and a row claimed by a run that died before recording
    /// its outcome is reported as interrupted rather than silently retried or counted a success.</summary>
    [Fact]
    public async Task A_row_left_claimed_by_a_dead_run_is_reported_as_interrupted()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        var jobId = await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        // A Running job with row 2 claimed but never resolved -- exactly the state a process that
        // died between the claim and the command leaves behind.
        var job = await db.ImportJobs.SingleAsync(j => j.Id == jobId);
        job.Claim(Now);
        db.ImportJobRows.Add(ImportJobRow.Claim(jobId, tenant.OrganizationId, 2));
        await db.SaveChangesAsync();

        host.FileReader.Returns(
            ImportTestSeed.ProductHeaders,
            ImportTestSeed.ProductRow("Interrupted Row"),
            ImportTestSeed.ProductRow("Clean Row"));

        host.Clock.Advance(TimeSpan.FromMinutes(5));
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var finished = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, finished.Status);
        Assert.Equal(1, finished.SucceededRowCount);
        Assert.Equal(1, finished.FailedRowCount);

        var interrupted = Assert.Single(await LoadRowsAsync(host, jobId, ImportJobRowStatus.Failed));
        Assert.Equal(2, interrupted.RowNumber);
        Assert.Contains("interrupted", interrupted.Message!, StringComparison.OrdinalIgnoreCase);

        // The interrupted row was never retried: only the clean row produced a product.
        Assert.Equal(["Clean Row"], await host.NewDbContext().Products.Select(p => p.Name).ToListAsync());
    }

    /// <summary>Cancellation stops between rows and keeps what already landed -- see
    /// CancelImportJobCommand for why nothing is rolled back.</summary>
    [Fact]
    public async Task Cancellation_stops_the_run_and_keeps_the_rows_that_already_landed()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        var jobId = await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        var job = await db.ImportJobs.SingleAsync(j => j.Id == jobId);
        job.RequestCancellation();
        await db.SaveChangesAsync();

        host.FileReader.Returns(
            ImportTestSeed.ProductHeaders,
            ImportTestSeed.ProductRow("Never Imported"),
            ImportTestSeed.ProductRow("Also Never"));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var cancelled = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CompletedAt);
        Assert.Empty(await host.NewDbContext().Products.ToListAsync());
    }

    /// <summary>
    /// <b>Decision B's answer to "is permission re-checked at execution time?" -- yes, on every row.</b>
    /// The user holds ImportJobManage (so the job was legitimately queued) but not
    /// Catalog.Product.Manage, so AuthorizationBehavior rejects the very first CreateProductCommand.
    /// The job stops rather than emitting one identical error per row.
    /// </summary>
    [Fact]
    public async Task A_revoked_permission_stops_the_job_at_the_first_row_rather_than_failing_every_row()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db, PermissionKeys.ImportJobManage);
        var jobId = await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        host.FileReader.Returns(
            ImportTestSeed.ProductHeaders,
            ImportTestSeed.ProductRow("One"),
            ImportTestSeed.ProductRow("Two"),
            ImportTestSeed.ProductRow("Three"));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Contains("no longer has permission", job.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(PermissionKeys.ProductManage, job.FailureReason!, StringComparison.Ordinal);

        Assert.Single(await LoadRowsAsync(host, jobId));
        Assert.Empty(await host.NewDbContext().Products.ToListAsync());
    }

    /// <summary>The acting identity reaches AuditBehavior, so imported rows are attributed to the
    /// person who started the import rather than to nobody.</summary>
    [Fact]
    public async Task Imported_contacts_are_audited_against_the_user_who_started_the_import()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Supplier, ImportMode.CreateNew, Now, host.FileStorage);

        host.FileReader.Returns(ImportTestSeed.SupplierHeaders, ImportTestSeed.SupplierRow("Audited Supplier"));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var audit = Assert.Single(await host.NewDbContext().Audits.ToListAsync());
        Assert.Equal(tenant.AdminUserId, audit.UserId);
        Assert.Equal("Create", audit.Action);
    }

    /// <summary>NFR-4.3's "notified on completion" -- to the initiator's own registered address, never
    /// to anything caller-supplied (see ImportJobProcessor.NotifyAsync).</summary>
    [Fact]
    public async Task The_initiator_is_emailed_at_their_own_registered_address_when_the_job_finishes()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        host.FileReader.Returns(ImportTestSeed.ProductHeaders, ImportTestSeed.ProductRow("Notified"));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var expectedAddress = await host.NewDbContext().Users
            .Where(u => u.Id == tenant.AdminUserId).Select(u => u.Email).SingleAsync();

        var mail = Assert.Single(host.EmailSender.SentEmails);
        Assert.Equal(expectedAddress, mail.To);
        Assert.Contains("Product import completed", mail.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reports_nothing_to_do_when_no_job_is_queued()
    {
        using var host = new ImportTestHost(Now);
        await ImportTestSeed.SeedAsync(host.NewDbContext());

        Assert.False(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));
    }

    /// <summary>A Running job whose runner is still alive must not be stolen -- only an expired lease
    /// makes it re-claimable.</summary>
    [Fact]
    public async Task A_running_job_with_a_fresh_heartbeat_is_not_reclaimed()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        var jobId = await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        var job = await db.ImportJobs.SingleAsync(j => j.Id == jobId);
        job.Claim(Now);
        await db.SaveChangesAsync();

        host.Clock.Advance(TimeSpan.FromSeconds(30));

        Assert.False(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));
    }

    /// <summary>
    /// Phase 21b's retention sweep, applied to the leak Phase 21a shipped: nothing in the tree ever
    /// deleted an import's uploaded workbook, so every one stayed on disk forever. The job row and
    /// its per-row results survive the purge -- only the blob goes.
    /// </summary>
    [Fact]
    public async Task Retention_deletes_a_finished_imports_uploaded_file()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        var jobId = await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        host.FileReader.Returns(ImportTestSeed.ProductHeaders, ImportTestSeed.ProductRow("Salted Cashew"));
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var storageKey = (await LoadJobAsync(host, jobId)).StorageKey;
        Assert.True(host.FileStorage.Contains(storageKey));

        // A day after the import finished, the upload is still there.
        host.Clock.SetUtcNow(Now + TimeSpan.FromDays(1));
        await host.NewProcessor().SweepAsync(CancellationToken.None);
        Assert.True(host.FileStorage.Contains(storageKey));

        host.Clock.SetUtcNow(Now + TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        await host.NewProcessor().SweepAsync(CancellationToken.None);

        Assert.False(host.FileStorage.Contains(storageKey));

        var job = await LoadJobAsync(host, jobId);
        Assert.NotNull(job.ArtifactPurgedAt);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Single(await LoadRowsAsync(host, jobId));
    }

    /// <summary>A job that is still queued or running has an upload the runner has not read yet;
    /// deleting it would be the one way this sweep could break an import.</summary>
    [Fact]
    public async Task Retention_leaves_an_unfinished_imports_upload_alone()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db);
        var jobId = await ImportTestSeed.QueueJobAsync(db, tenant, ImportEntityType.Product, ImportMode.CreateNew, Now, host.FileStorage);

        host.Clock.SetUtcNow(Now + TimeSpan.FromDays(30));
        await host.NewProcessor().SweepAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Null(job.ArtifactPurgedAt);
        Assert.True(host.FileStorage.Contains(job.StorageKey));
    }

    private static async Task<ImportJob> LoadJobAsync(ImportTestHost host, Guid jobId) =>
        await host.NewDbContext().ImportJobs.SingleAsync(j => j.Id == jobId);

    private static async Task<List<ImportJobRow>> LoadRowsAsync(
        ImportTestHost host, Guid jobId, ImportJobRowStatus? status = null)
    {
        var query = host.NewDbContext().ImportJobRows.Where(r => r.ImportJobId == jobId);
        if (status is { } value)
        {
            query = query.Where(r => r.Status == value);
        }

        return await query.OrderBy(r => r.RowNumber).ToListAsync();
    }
}
