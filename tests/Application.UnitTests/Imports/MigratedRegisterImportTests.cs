using ErpApp.Application.Common.Security;
using ErpApp.Application.Imports;
using ErpApp.Application.Imports.Commands.CreateImportJob;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Imports;

/// <summary>
/// Phase 21c -- migrated tax-register import (FR-2.10), driven end to end through the real
/// <see cref="ImportJobProcessor"/> and a real DI container (<see cref="ImportTestHost"/>), so every
/// row travels the full six-behavior MediatR pipeline. Stubbing <c>ISender</c> would make the
/// permission and validation assertions below vacuous -- see the host's own doc comment.
///
/// <para><b>Decision C in one sentence:</b> these are ordinary <c>ImportJob</c>s. Everything the
/// master-data importers get -- claim-then-act per row, resume after a crash, cancellation,
/// per-row error reporting, retention -- these get for free, which is why this file tests what is
/// <i>new</i> rather than re-testing the runner.</para>
///
/// <para><b>What the InMemory provider cannot prove here:</b> it enforces no unique index, so the
/// (OrganizationId, DocumentCode) constraint that makes re-import safe under two concurrent runners
/// is unreachable from these tests. The handler's own pre-check -- the half that produces the
/// readable per-row message -- is covered below; the index itself is in the migration and was
/// verified against real SQL Server.</para>
/// </summary>
public class MigratedRegisterImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] SalesHeaders =
    [
        "Date", "Document No", "Customer Name", "Customer PAN", "Total Sales Value",
        "Tax-Exempt Sales Value", "Taxable Sales Value", "VAT Amount",
        "Export Value", "Export Country", "Export Declaration No", "Export Declaration Date",
    ];

    private static readonly string[] PurchaseHeaders =
    [
        "Date", "Bill No", "Import Declaration No", "Supplier Name", "Supplier PAN", "Tax-Exempt Value",
        "Taxable Non-Capital (Local) Value", "Taxable Non-Capital (Local) VAT",
        "Taxable Non-Capital (Import) Value", "Taxable Non-Capital (Import) VAT",
        "Taxable Capital Value", "Taxable Capital VAT",
    ];

    [Fact]
    public async Task Imports_sales_rows_and_completes()
    {
        using var host = new ImportTestHost(Now);
        var (tenant, jobId) = await QueueSalesJobAsync(host);

        host.FileReader.Returns(
            SalesHeaders,
            SalesRow("2024-07-30", "INV-0912", total: "113", taxable: "100", vat: "13"),
            SalesRow("2024-07-31", "INV-0913", total: "226", taxable: "200", vat: "26"),
            // A sales return, entered the way the template's instructions say: negative values.
            SalesRow("2024-08-01", "CN-0031", total: "-113", taxable: "-100", vat: "-13"));

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(3, job.SucceededRowCount);
        Assert.Equal(0, job.FailedRowCount);

        var entries = await host.NewDbContext().MigratedSalesRegisterEntries
            .Where(x => x.OrganizationId == tenant.OrganizationId)
            .OrderBy(x => x.DocumentCode)
            .ToListAsync();

        Assert.Equal(["CN-0031", "INV-0912", "INV-0913"], entries.Select(x => x.DocumentCode));
        Assert.Equal(new DateOnly(2024, 7, 30), entries.Single(x => x.DocumentCode == "INV-0912").Date);
        Assert.Equal(-113m, entries.Single(x => x.DocumentCode == "CN-0031").TotalValue);
    }

    /// <summary>
    /// <b>The claim that matters most, asserted at the level a future phase would break it.</b> A
    /// completed import writes no GlJournalEntry, no GlLine, no StockLedgerEntry, no StockMovement
    /// and no Payment -- FR-2.10's "without needing to recreate every historical transaction as a
    /// full document", made concrete. Also verified live against real SQL Server with sqlcmd, since
    /// this is exactly the kind of claim an InMemory test could be made to pass while the real thing
    /// regressed.
    /// </summary>
    [Fact]
    public async Task An_import_writes_nothing_to_the_general_ledger_stock_or_payments()
    {
        using var host = new ImportTestHost(Now);
        var (tenant, _) = await QueueSalesJobAsync(host);

        host.FileReader.Returns(
            SalesHeaders,
            SalesRow("2024-07-30", "INV-0912", total: "113", taxable: "100", vat: "13"),
            SalesRow("2024-07-31", "INV-0913", total: "226", taxable: "200", vat: "26"));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var db = host.NewDbContext();
        Assert.Equal(2, await db.MigratedSalesRegisterEntries.CountAsync(x => x.OrganizationId == tenant.OrganizationId));
        Assert.Equal(0, await db.GlJournalEntries.CountAsync(x => x.OrganizationId == tenant.OrganizationId));
        Assert.Equal(0, await db.GlLines.CountAsync());
        Assert.Equal(0, await db.StockLedgerEntries.CountAsync(x => x.OrganizationId == tenant.OrganizationId));
        Assert.Equal(0, await db.StockMovements.CountAsync(x => x.OrganizationId == tenant.OrganizationId));
        Assert.Equal(0, await db.Payments.CountAsync(x => x.OrganizationId == tenant.OrganizationId));
        Assert.Equal(0, await db.Invoices.CountAsync(x => x.OrganizationId == tenant.OrganizationId));
    }

    /// <summary>Partial success is a Completed job (21a's Decision C), and each rejection names the
    /// spreadsheet's own row number and the offending column.</summary>
    [Fact]
    public async Task Bad_rows_are_reported_by_row_number_and_column_and_the_good_ones_still_import()
    {
        using var host = new ImportTestHost(Now);
        var (tenant, jobId) = await QueueSalesJobAsync(host);

        host.FileReader.Returns(
            SalesHeaders,
            SalesRow("2024-07-30", "INV-0912", total: "113", taxable: "100", vat: "13"),
            SalesRow("not-a-date", "INV-0913", total: "113", taxable: "100", vat: "13"),
            SalesRow("2024-08-01", documentNo: "", total: "113", taxable: "100", vat: "13"),
            SalesRow("2024-08-02", "INV-0915", total: "226", taxable: "200", vat: "26"));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(2, job.SucceededRowCount);
        Assert.Equal(2, job.FailedRowCount);

        var failures = await host.NewDbContext().ImportJobRows
            .Where(r => r.ImportJobId == jobId && r.Status == ImportJobRowStatus.Failed)
            .ToListAsync();

        // Header is row 1, so the second data row is row 3 -- the number the user sees in Excel.
        var badDate = failures.Single(r => r.RowNumber == 3);
        Assert.Equal("Date", badDate.ColumnName);
        Assert.Contains("not-a-date", badDate.Message!, StringComparison.Ordinal);

        Assert.Equal("Document No", failures.Single(r => r.RowNumber == 4).ColumnName);

        Assert.Equal(2, await host.NewDbContext().MigratedSalesRegisterEntries
            .CountAsync(x => x.OrganizationId == tenant.OrganizationId));
    }

    /// <summary>
    /// Re-import safety. A cutover import is the upload a user is most likely to run twice by
    /// accident, and a silent duplicate would double the tenant's filed statutory sales -- so the
    /// second upload rejects every repeated row by document number rather than duplicating or
    /// silently replacing. The rejected rows are still a Completed job carrying a readable message,
    /// so a file mixing new rows with already-imported ones imports exactly the new ones.
    /// </summary>
    [Fact]
    public async Task A_second_upload_rejects_rows_already_imported_and_still_imports_the_new_ones()
    {
        using var host = new ImportTestHost(Now);
        var (tenant, _) = await QueueSalesJobAsync(host);

        host.FileReader.Returns(
            SalesHeaders,
            SalesRow("2024-07-30", "INV-0912", total: "113", taxable: "100", vat: "13"));
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var secondJobId = await ImportTestSeed.QueueJobAsync(
            host.NewDbContext(), tenant, ImportEntityType.MigratedSalesRegister, ImportMode.CreateNew,
            Now, host.FileStorage);

        host.FileReader.Returns(
            SalesHeaders,
            SalesRow("2024-07-30", "INV-0912", total: "113", taxable: "100", vat: "13"),
            SalesRow("2024-08-05", "INV-0999", total: "113", taxable: "100", vat: "13"));
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, secondJobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(1, job.SucceededRowCount);
        Assert.Equal(1, job.FailedRowCount);

        var failure = await host.NewDbContext().ImportJobRows
            .SingleAsync(r => r.ImportJobId == secondJobId && r.Status == ImportJobRowStatus.Failed);
        Assert.Contains("already been imported", failure.Message!, StringComparison.Ordinal);

        Assert.Equal(2, await host.NewDbContext().MigratedSalesRegisterEntries
            .CountAsync(x => x.OrganizationId == tenant.OrganizationId));
    }

    /// <summary>
    /// A migrated row is dated before the tenant's accounting start date and almost certainly before
    /// any lock date. Gating it would make the feature unusable for its only purpose, so
    /// <c>CreateMigratedSalesRegisterEntryCommand</c> implements neither lock-date marker interface
    /// and <c>LockDateBehavior</c> skips it -- a decision, not an oversight, safe because the row
    /// posts nothing. This test is what stops someone "fixing" that by adding the marker.
    /// </summary>
    [Fact]
    public async Task A_row_dated_before_the_organizations_lock_date_still_imports()
    {
        using var host = new ImportTestHost(Now);
        var (tenant, jobId) = await QueueSalesJobAsync(host);

        var db = host.NewDbContext();
        var organization = await db.Organizations.SingleAsync(o => o.Id == tenant.OrganizationId);
        organization.SetLockDate(new DateOnly(2026, 3, 31));
        await db.SaveChangesAsync(CancellationToken.None);

        host.FileReader.Returns(
            SalesHeaders,
            SalesRow("2024-07-30", "INV-0912", total: "113", taxable: "100", vat: "13"));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(1, job.SucceededRowCount);
    }

    /// <summary>
    /// Decision D's payoff, and 21a's corollary restated: the feature-level <c>ImportJobManage</c>
    /// key does not stand in for the per-row key. A user who may enqueue an import but has never
    /// been granted <c>MigratedRegisterManage</c> writes nothing, and the job stops with a message
    /// naming the problem rather than emitting the same 403 once per row.
    /// </summary>
    [Fact]
    public async Task Without_the_per_row_key_the_job_fails_and_writes_nothing()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db, PermissionKeys.ImportJobManage);
        var jobId = await ImportTestSeed.QueueJobAsync(
            db, tenant, ImportEntityType.MigratedSalesRegister, ImportMode.CreateNew, Now, host.FileStorage);

        host.FileReader.Returns(
            SalesHeaders,
            SalesRow("2024-07-30", "INV-0912", total: "113", taxable: "100", vat: "13"));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Contains("permission", job.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await host.NewDbContext().MigratedSalesRegisterEntries.ToListAsync());
    }

    /// <summary>An exact PAN match links the row to the existing Contact; anything else leaves it
    /// standing on its free-text party alone. Nothing here ever creates a Contact -- minting master
    /// data to satisfy a report column is the failure mode Decision A set out to avoid.</summary>
    [Fact]
    public async Task An_exact_pan_match_links_an_existing_contact_and_no_contact_is_ever_created()
    {
        using var host = new ImportTestHost(Now);
        var (tenant, _) = await QueueSalesJobAsync(host);

        var db = host.NewDbContext();
        var contact = Contact.Create(
            tenant.OrganizationId, ContactType.Customer, "C0001", "Himalayan Traders Private Limited",
            null, "301234567", null, null, tenant.ContactGroupId, 0m);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(CancellationToken.None);

        var contactCountBefore = await host.NewDbContext().Contacts.CountAsync();

        host.FileReader.Returns(
            SalesHeaders,
            SalesRow("2024-07-30", "INV-0912", total: "113", taxable: "100", vat: "13", pan: "301234567"),
            SalesRow("2024-07-31", "INV-0913", total: "113", taxable: "100", vat: "13", pan: "309999999"),
            SalesRow("2024-08-01", "INV-0914", total: "113", taxable: "100", vat: "13", pan: null));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var entries = await host.NewDbContext().MigratedSalesRegisterEntries.ToListAsync();
        Assert.Equal(contact.Id, entries.Single(x => x.DocumentCode == "INV-0912").ContactId);
        Assert.Null(entries.Single(x => x.DocumentCode == "INV-0913").ContactId);
        Assert.Null(entries.Single(x => x.DocumentCode == "INV-0914").ContactId);

        Assert.Equal(contactCountBefore, await host.NewDbContext().Contacts.CountAsync());
    }

    [Fact]
    public async Task Imports_purchase_rows_across_the_three_taxable_pairs()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(
            db, PermissionKeys.ImportJobManage, PermissionKeys.MigratedRegisterManage);
        var jobId = await ImportTestSeed.QueueJobAsync(
            db, tenant, ImportEntityType.MigratedPurchaseRegister, ImportMode.CreateNew, Now, host.FileStorage);

        host.FileReader.Returns(
            PurchaseHeaders,
            ["2024-07-28", "BILL-4471", null, "Everest Supplies", "302345678", "0", "80000", "10400", "0", "0", "0", "0"],
            ["2024-07-29", "BILL-4472", "PP-991", "Overseas Co", null, "0", "0", "0", "50000", "6500", "0", "0"],
            ["2024-07-30", "BILL-4473", null, "Machinery Nepal", null, "0", "0", "0", "0", "0", "200000", "26000"]);

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(3, job.SucceededRowCount);

        var entries = await host.NewDbContext().MigratedPurchaseRegisterEntries
            .Where(x => x.OrganizationId == tenant.OrganizationId)
            .ToListAsync();

        Assert.Equal(80000m, entries.Single(x => x.DocumentCode == "BILL-4471").TaxableNonCapitalLocalValue);
        Assert.Equal("PP-991", entries.Single(x => x.DocumentCode == "BILL-4472").ImportDeclarationNo);
        Assert.Equal(26000m, entries.Single(x => x.DocumentCode == "BILL-4473").TaxableCapitalVat);
        Assert.Empty(await host.NewDbContext().PurchaseBills.ToListAsync());
    }

    /// <summary>Migrated rows are create-only, and the upload is rejected as one whole-file mistake
    /// rather than one identical row error repeated N times.</summary>
    [Theory]
    [InlineData(ImportEntityType.MigratedSalesRegister)]
    [InlineData(ImportEntityType.MigratedPurchaseRegister)]
    public void Update_mode_is_rejected_at_upload_for_a_migrated_register(ImportEntityType entityType)
    {
        using var content = new MemoryStream([0x50, 0x4B]);
        var command = new CreateImportJobCommand(
            Guid.NewGuid(), entityType, ImportMode.UpdateExisting, "history.xlsx", 1024, content);

        var result = new CreateImportJobCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateImportJobCommand.Mode));
    }

    [Fact]
    public void Create_mode_is_accepted_for_a_migrated_register()
    {
        using var content = new MemoryStream([0x50, 0x4B]);
        var command = new CreateImportJobCommand(
            Guid.NewGuid(), ImportEntityType.MigratedSalesRegister, ImportMode.CreateNew, "history.xlsx", 1024, content);

        Assert.True(new CreateImportJobCommandValidator().Validate(command).IsValid);
    }

    /// <summary>The template a user downloads and the parser that reads it back are one declaration
    /// (<c>ImportTemplateDefinition</c>), so this asserts the property that makes that worth having:
    /// the sample row is positionally aligned with the columns, and every required column is one the
    /// importer actually reads.</summary>
    [Theory]
    [InlineData(ImportEntityType.MigratedSalesRegister)]
    [InlineData(ImportEntityType.MigratedPurchaseRegister)]
    public void The_template_is_internally_consistent(ImportEntityType entityType)
    {
        IEntityImporter importer = entityType == ImportEntityType.MigratedSalesRegister
            ? new MigratedSalesRegisterImporter(null!)
            : new MigratedPurchaseRegisterImporter(null!);

        var template = importer.Template;

        Assert.Equal(entityType, template.EntityType);
        Assert.Equal(template.Columns.Count, template.SampleRow.Count);
        Assert.Equal(template.Columns.Count, template.HeaderTexts.Count);
        Assert.All(template.Columns.Where(c => c.Required), c => Assert.EndsWith(
            "**", template.HeaderTexts[template.Columns.ToList().IndexOf(c)], StringComparison.Ordinal));
        Assert.Contains(template.Instructions, i => i.Contains("General Ledger", StringComparison.Ordinal));
        Assert.Contains(template.Instructions, i => i.Contains("NEGATIVE", StringComparison.Ordinal));
    }

    private static async Task<(ImportTenant Tenant, Guid JobId)> QueueSalesJobAsync(ImportTestHost host)
    {
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(
            db, PermissionKeys.ImportJobManage, PermissionKeys.MigratedRegisterManage);
        var jobId = await ImportTestSeed.QueueJobAsync(
            db, tenant, ImportEntityType.MigratedSalesRegister, ImportMode.CreateNew, Now, host.FileStorage);
        return (tenant, jobId);
    }

    private static string?[] SalesRow(
        string date,
        string documentNo,
        string total,
        string taxable,
        string vat,
        string? pan = "301234567",
        string customerName = "Himalayan Traders Private Limited") =>
        [date, documentNo, customerName, pan, total, "0", taxable, vat, "0", null, null, null];

    private static async Task<ImportJob> LoadJobAsync(ImportTestHost host, Guid jobId) =>
        await host.NewDbContext().ImportJobs.SingleAsync(j => j.Id == jobId);
}
