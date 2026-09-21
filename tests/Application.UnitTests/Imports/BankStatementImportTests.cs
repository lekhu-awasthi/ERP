using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Imports;
using ErpApp.Application.Imports.Commands.CreateImportJob;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Imports;

/// <summary>
/// Phase 55 -- bank statement import, driven end to end through the real
/// <see cref="ImportJobProcessor"/> and a real DI container (<see cref="ImportTestHost"/>), so
/// every row travels the full MediatR pipeline and the permission check on each row is a real one.
///
/// <para><b>Decision A is what this file is really about.</b> The kickoff's doubt was that a
/// statement line "resolves into no command" and so might not belong on phase 38's machinery. It
/// is an ordinary importer, and these tests are the demonstration: the row ledger, the per-row
/// error reporting, the dry run and the resume all work without a line of new runner code. So this
/// file tests what is <i>new</i> -- the per-run account, the amount rules, and the claim that an
/// import posts nothing -- rather than re-testing the runner.</para>
/// </summary>
public class BankStatementImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] Headers = ["Date", "Deposit", "Withdrawal", "Description"];

    [Fact]
    public async Task Imports_deposits_and_withdrawals_against_the_run_s_account()
    {
        using var host = new ImportTestHost(Now);
        var (tenant, accountId, jobId) = await QueueJobAsync(host);

        host.FileReader.Returns(
            Headers,
            Row("2026-09-01", deposit: "1500", description: "Salary credit"),
            Row("2026-09-02", withdrawal: "250.75", description: "ATM withdrawal"),
            Row("2026-09-03", deposit: "12.5", description: null));

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(3, job.SucceededRowCount);
        Assert.Equal(0, job.FailedRowCount);

        var lines = await LinesAsync(host, tenant.OrganizationId);

        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.Equal(accountId, l.BankAccountId));

        // The direction survives the round trip through the EF value converter, which is the one
        // place a signed decimal becomes a StatementAmount again.
        Assert.Equal(1500m, lines[0].Amount.Signed);
        Assert.Equal(-250.75m, lines[1].Amount.Signed);
        Assert.Equal(250.75m, lines[1].Amount.WithdrawalAmount);
        Assert.Null(lines[2].Description);

        // Every line records the run that made it, which is what makes a doubled upload one
        // deletion rather than a hunt through the list.
        Assert.All(lines, l => Assert.Equal(jobId, l.ImportJobId));
    }

    /// <summary>
    /// <b>The claim that matters most, asserted at the level a future phase would break it.</b>
    /// Importing a statement changes no balance: it writes no GlJournalEntry, no GlLine, no
    /// Payment and no stock movement. A statement line that posted would double-count against the
    /// tenant's own documents by construction, and the whole point of a reconciliation is that the
    /// two records are independent.
    /// </summary>
    [Fact]
    public async Task An_import_writes_nothing_to_the_general_ledger_payments_or_stock()
    {
        using var host = new ImportTestHost(Now);
        var (tenant, _, _) = await QueueJobAsync(host);

        host.FileReader.Returns(
            Headers,
            Row("2026-09-01", deposit: "1500", description: "Salary credit"),
            Row("2026-09-02", withdrawal: "250.75", description: "ATM withdrawal"));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var db = host.NewDbContext();
        Assert.Equal(2, await db.BankStatementLines.CountAsync(x => x.OrganizationId == tenant.OrganizationId));
        Assert.Equal(0, await db.GlJournalEntries.CountAsync(x => x.OrganizationId == tenant.OrganizationId));
        Assert.Equal(0, await db.GlLines.CountAsync());
        Assert.Equal(0, await db.Payments.CountAsync(x => x.OrganizationId == tenant.OrganizationId));
        Assert.Equal(0, await db.StockLedgerEntries.CountAsync(x => x.OrganizationId == tenant.OrganizationId));
        Assert.Equal(0, await db.StockMovements.CountAsync(x => x.OrganizationId == tenant.OrganizationId));
        Assert.Equal(0, await db.Cheques.CountAsync(x => x.OrganizationId == tenant.OrganizationId));
    }

    /// <summary>
    /// The three rejections the reference product's own validate leg answers, probed live on
    /// 2026-09-21 and reproduced here -- plus the fourth it does <i>not</i>: it accepts a negative
    /// Deposit verbatim as <c>dr_amount: -40</c>, which is a defect and is refused here.
    ///
    /// <para>Partial success is a Completed job (phase 21a's Decision C), so the good rows import
    /// beside the bad ones, each rejection naming its spreadsheet row.</para>
    /// </summary>
    [Fact]
    public async Task Bad_rows_are_reported_by_row_number_and_the_good_ones_still_import()
    {
        using var host = new ImportTestHost(Now);
        var (tenant, _, jobId) = await QueueJobAsync(host);

        host.FileReader.Returns(
            Headers,
            Row("2026-09-01", deposit: "1500", description: "good"),
            Row("not-a-date", deposit: "10", description: "invalid date"),
            Row("2026-09-02", description: "no amount at all"),
            Row("2026-09-03", deposit: "100", withdrawal: "100", description: "both amounts"),
            Row("2026-09-04", deposit: "-40", description: "negative deposit"));

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(1, job.SucceededRowCount);
        Assert.Equal(4, job.FailedRowCount);

        var failures = await host.NewDbContext().ImportJobRows
            .Where(r => r.ImportJobId == jobId && r.Status == ImportJobRowStatus.Failed)
            .OrderBy(r => r.RowNumber)
            .ToListAsync();

        // Row numbers are the spreadsheet's own, header included -- row 2 is the first data row.
        Assert.Equal([3, 4, 5, 6], failures.Select(f => f.RowNumber));
        Assert.Contains("not a valid date", failures[0].Message!, StringComparison.Ordinal);
        Assert.Contains("must carry a deposit or a withdrawal", failures[1].Message!, StringComparison.Ordinal);
        Assert.Contains("either a deposit or a withdrawal, not both", failures[2].Message!, StringComparison.Ordinal);
        Assert.Contains("positive amount", failures[3].Message!, StringComparison.Ordinal);

        Assert.Single(await LinesAsync(host, tenant.OrganizationId));
    }

    /// <summary>
    /// Phase 38's whole claim, on this importer: the dry run and the real run resolve rows the same
    /// way, so the validate pass names exactly the rows the apply pass will skip and nothing is
    /// written until the user confirms.
    /// </summary>
    [Fact]
    public async Task The_dry_run_names_the_bad_row_and_the_apply_pass_skips_exactly_that_row()
    {
        using var host = new ImportTestHost(Now);
        var (tenant, _, jobId) = await QueueJobAsync(host, reviewBeforeApply: true);

        host.FileReader.Returns(
            Headers,
            Row("2026-09-01", deposit: "1500", description: "good"),
            Row("2026-09-02", deposit: "100", withdrawal: "100", description: "both amounts"),
            Row("2026-09-03", withdrawal: "60", description: "also good"));

        // The validate pass.
        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var reviewed = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.PendingConfirmation, reviewed.Status);
        Assert.Equal(1, reviewed.FailedRowCount);
        Assert.Empty(await LinesAsync(host, tenant.OrganizationId));

        var claimedInReview = await host.NewDbContext().ImportJobRows
            .Where(r => r.ImportJobId == jobId)
            .Select(r => r.RowNumber)
            .ToListAsync();

        // The validate pass claims ONLY the row it rejects -- that is what makes the apply pass
        // skip it through the same mechanism that makes a crashed import resumable.
        Assert.Equal([3], claimedInReview);

        reviewed.ConfirmReview(Now);
        var db = host.NewDbContext();
        db.ImportJobs.Update(reviewed);
        await db.SaveChangesAsync();

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var applied = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Completed, applied.Status);
        Assert.Equal(2, applied.SucceededRowCount);
        Assert.Equal(1, applied.FailedRowCount);

        var lines = await LinesAsync(host, tenant.OrganizationId);
        Assert.Equal([1500m, -60m], lines.Select(l => l.Amount.Signed));
    }

    /// <summary>
    /// A statement imported into an account of the wrong kind is refused. The run-level check is in
    /// <c>CreateImportJobCommandHandler</c>, but the per-row command re-checks because it must hold
    /// for any caller -- this asserts the second one, by queueing a job straight into the table the
    /// way a caller bypassing the handler would.
    /// </summary>
    [Fact]
    public async Task A_statement_cannot_be_imported_into_an_ordinary_chart_of_accounts_row()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(
            db, PermissionKeys.ImportJobManage, PermissionKeys.BankStatementManage);

        var revenue = await SeedAccountAsync(db, tenant.OrganizationId, "Sales Revenue", AccountKind.Other);
        var jobId = await ImportTestSeed.QueueJobAsync(
            db, tenant, ImportEntityType.BankStatement, ImportMode.CreateNew, Now, host.FileStorage,
            bankAccountId: revenue);

        host.FileReader.Returns(Headers, Row("2026-09-01", deposit: "1500", description: "good"));

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(1, job.FailedRowCount);
        Assert.Empty(await LinesAsync(host, tenant.OrganizationId));
    }

    /// <summary>
    /// AuthorizationBehavior runs on every row, because the runner sends each create command under
    /// the initiating user's identity (phase 21a) -- so a user holding ImportJobManage but not
    /// BankStatementManage cannot import a statement, and the refusal names the exact key.
    ///
    /// <para>The job <b>aborts</b> rather than failing every row: a missing permission is a
    /// whole-job condition, so <c>ImportJobProcessor</c> marks the first row and stops instead of
    /// writing N copies of one message. That is phase 21a's existing decision, asserted here
    /// because it is the behaviour this importer inherits rather than one it chooses.</para>
    /// </summary>
    [Fact]
    public async Task The_job_is_refused_when_the_initiating_user_lacks_the_statement_key()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(db, PermissionKeys.ImportJobManage);
        var accountId = await SeedAccountAsync(db, tenant.OrganizationId, "Nabil Bank", AccountKind.Bank);
        var jobId = await ImportTestSeed.QueueJobAsync(
            db, tenant, ImportEntityType.BankStatement, ImportMode.CreateNew, Now, host.FileStorage,
            bankAccountId: accountId);

        host.FileReader.Returns(
            Headers,
            Row("2026-09-01", deposit: "1500", description: "good"),
            Row("2026-09-02", withdrawal: "60", description: "also good"));

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var job = await LoadJobAsync(host, jobId);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Equal(0, job.SucceededRowCount);
        Assert.Empty(await LinesAsync(host, tenant.OrganizationId));

        var failure = await host.NewDbContext().ImportJobRows
            .Where(r => r.ImportJobId == jobId)
            .OrderBy(r => r.RowNumber)
            .FirstAsync();

        // The exact key, not a generic "forbidden" -- the same bar the manual E2E's 403 has to
        // meet.
        Assert.Contains(PermissionKeys.BankStatementManage, failure.Message!, StringComparison.Ordinal);
        Assert.Contains(PermissionKeys.BankStatementManage, job.FailureReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reference product accepts two identical rows silently (probed live), and so do we -- two
    /// identical ATM withdrawals on one day are two real lines, so any uniqueness rule wide enough
    /// to catch a duplicated upload also rejects genuine data. What makes the mistake cheap is
    /// ImportJobId, not a constraint that cannot exist.
    /// </summary>
    [Fact]
    public async Task Two_identical_rows_are_both_imported()
    {
        using var host = new ImportTestHost(Now);
        var (tenant, _, _) = await QueueJobAsync(host);

        host.FileReader.Returns(
            Headers,
            Row("2026-09-01", withdrawal: "500", description: "ATM withdrawal"),
            Row("2026-09-01", withdrawal: "500", description: "ATM withdrawal"));

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        Assert.Equal(2, (await LinesAsync(host, tenant.OrganizationId)).Count);
    }

    /// <summary>
    /// The template a user downloads and the parser that reads it back are one declaration
    /// (<c>ImportTemplateDefinition</c>), so this pins the column set that came off the reference
    /// product's own "Two Amount Column" file rather than the parser's private opinion of it.
    /// </summary>
    [Fact]
    public void The_template_is_the_reference_product_s_two_amount_column_shape()
    {
        var template = new BankStatementImporter().Template;

        Assert.Equal(
            ["Date", "Deposit", "Withdrawal", "Description"],
            template.Columns.Select(c => c.Name));

        // Only Date is required as a column; the two amounts are per-row rules, because a row is
        // either a deposit or a withdrawal and neither column is always populated.
        Assert.Equal(["Date"], template.Columns.Where(c => c.Required).Select(c => c.Name));

        Assert.Contains(
            template.Instructions,
            i => i.Contains("POSITIVE", StringComparison.Ordinal));
        Assert.Contains(
            template.Instructions,
            i => i.Contains("General Ledger", StringComparison.Ordinal));
    }

    private static string?[] Row(
        string date, string? deposit = null, string? withdrawal = null, string? description = null) =>
        [date, deposit, withdrawal, description];

    private static async Task<(ImportTenant Tenant, Guid AccountId, Guid JobId)> QueueJobAsync(
        ImportTestHost host, bool reviewBeforeApply = false)
    {
        var db = host.NewDbContext();
        var tenant = await ImportTestSeed.SeedAsync(
            db, PermissionKeys.ImportJobManage, PermissionKeys.BankStatementManage);
        var accountId = await SeedAccountAsync(db, tenant.OrganizationId, "Nabil Bank", AccountKind.Bank);
        var jobId = await ImportTestSeed.QueueJobAsync(
            db, tenant, ImportEntityType.BankStatement, ImportMode.CreateNew, Now, host.FileStorage,
            reviewBeforeApply, accountId);
        return (tenant, accountId, jobId);
    }

    private static async Task<Guid> SeedAccountAsync(
        IAppDbContext db, Guid organizationId, string name, AccountKind kind)
    {
        var account = Account.Create(
            organizationId, $"BC{Guid.NewGuid():N}"[..8], name, AccountRootType.Asset, Guid.NewGuid(), kind);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private static async Task<List<BankStatementLine>> LinesAsync(ImportTestHost host, Guid organizationId) =>
        await host.NewDbContext().BankStatementLines
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Date)
            .ToListAsync();

    private static async Task<ImportJob> LoadJobAsync(ImportTestHost host, Guid jobId) =>
        await host.NewDbContext().ImportJobs.SingleAsync(j => j.Id == jobId);
}
