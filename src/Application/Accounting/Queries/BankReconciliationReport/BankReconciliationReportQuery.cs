using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Accounting.Queries.ListBankStatementLines;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.BankReconciliationReport;

/// <summary>
/// Phase 56 — the <b>Reconciliation Report</b> tab on a cash-and-bank account, read live on
/// 2026-09-21. Four figures as of one date, plus the two lists that explain the difference:
///
/// <code>
/// Balance In TIGG App        As of 21-09-2026   NPR 791
/// Balance in Cash In Hand    As of 21-09-2026   NPR 1,582
/// Difference                                    NPR -791
/// &gt; Unrecognized Transaction in Tigg App        NPR 0
/// &gt; Unrecognized Transaction in Bank            NPR 791
/// </code>
///
/// <para><b>One as-of date, not a range</b> — the live filter bar carries a single Date control, and
/// the two expandable sections are the unreconciled feeds with <c>time_stamp_$lte=&lt;date&gt;</c>.
/// That is the right shape for the subject: a reconciliation report answers "do the two records
/// agree <i>today</i>", and a balance is cumulative, not a period figure.</para>
///
/// <para><b>Which date, and phase 26a's rule.</b> The book side cuts off on <c>PostedAt</c>, because
/// that is the only date a GL posting has here and a report must show the same field it filters on;
/// the bank side cuts off on the statement line's own <c>Date</c>, which is the bank's value date.
/// The two are different fields because they are different records kept by different people — which
/// is the entire premise of reconciling them — so this is not an inconsistency to iron out. The
/// screen labels each column with its own date.</para>
///
/// <para><b>Both sides come from readers the matcher also uses</b>
/// (<see cref="BankBookTransactionReader"/> and the statement lines' own filter), so the report's
/// <i>Unrecognized</i> figures are the sums of exactly the rows the matcher offers to reconcile. By
/// construction, not by coincidence — phase 36's rule that two reports agree only through one shared
/// reader plus a test reading both on the same data, which
/// <c>BankReconciliationAgreementTests</c> is.</para>
///
/// <para><b>It rides <see cref="PermissionKeys.BankStatementView"/> rather than earning a key.</b>
/// The vendor's only reconciliation-specific key is <c>bank-reconciliation-export</c> — on the
/// export, not on the report — and this codebase has no "report Export" key to follow as a
/// precedent anywhere (<c>PermissionKeys</c> says so where the export-job keys are declared): an
/// export is gated by the report it exports. And the report is a bounded rollup over routine
/// daily-use working data, which CLAUDE.md's derivation rule puts at Admin+Member, the bar the two
/// keys phase 55 minted already sit at.</para>
/// </summary>
/// <param name="AsOfDate">Null means today, which is what the screen opens on.</param>
/// <param name="UnrecognizedPageSize">How many rows each of the two sections shows. The live screen
/// requests 15 and pages within the section.</param>
public sealed record BankReconciliationReportQuery(
    Guid OrganizationId,
    Guid BankAccountId,
    DateOnly? AsOfDate = null,
    int UnrecognizedPage = 1,
    int UnrecognizedPageSize = 15)
    : IRequest<BankReconciliationReportDto>, IRequirePermission, IOrganizationScoped
{
    /// <summary>
    /// Phase 57 -- the largest page either unreconciled section will serve, and the size the .xlsx
    /// export asks for. Higher than the app's usual 200 because an export of one screen's page
    /// would be a lie, and stated rather than unbounded because ClosedXML materialises every cell
    /// of every sheet before a byte is written (phase 21b). The sheet prints each section's real
    /// count beside its rows, so a truncated export says so in the artifact.
    /// </summary>
    public const int MaxUnrecognizedPageSize = 5000;

    public string PermissionKey => PermissionKeys.BankStatementView;
}

/// <param name="BookBalance">"Balance In TIGG App" — the net of every GL movement through this
/// account up to the as-of date, signed the account's way.</param>
/// <param name="BankBalance">"Balance in &lt;account&gt;" — the net of every imported statement line
/// up to the as-of date.</param>
/// <param name="Difference">Bank less book. Zero is the state the whole feature exists to
/// reach.</param>
/// <param name="UnreconciledBookTotal">"Unrecognized Transaction in Tigg App".</param>
/// <param name="UnreconciledBankTotal">"Unrecognized Transaction in Bank".</param>
public sealed record BankReconciliationReportDto(
    Guid BankAccountId,
    string AccountCode,
    string AccountName,
    DateOnly AsOfDate,
    decimal BookBalance,
    decimal BankBalance,
    decimal Difference,
    decimal UnreconciledBookTotal,
    int UnreconciledBookCount,
    decimal UnreconciledBankTotal,
    int UnreconciledBankCount,
    IReadOnlyList<BookTransactionDto> UnreconciledBookTransactions,
    IReadOnlyList<BankStatementLineListItem> UnreconciledStatementLines);
