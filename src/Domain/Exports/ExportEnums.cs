namespace ErpApp.Domain.Exports;

/// <summary>
/// What a data export contains. The five members are <b>exactly</b> the five categories
/// product-requirements.md FR-2.8 names -- "products, contacts, chart of accounts, ledger
/// transactions, stock movements" -- and that is a scope decision, not a coincidence.
///
/// <para><b>Why five and not all 82 DbSets.</b> Read literally, "full data backup" would mean every
/// table in <c>IAppDbContext</c>, including <c>AlertSendLog</c>, <c>Audit</c>, <c>ImportJobRow</c>
/// and <c>VerificationCode</c> -- an artifact nobody could read and nothing could restore, since
/// this codebase has no restore path at all. FR-2.8's own parenthesis is the narrower and more
/// useful reading, and it is the one this phase ships. See docs/phase-21b-status.md, Decision A.</para>
///
/// <para>Adding a sixth category is a new <c>IExportCategoryReader</c> plus one DI line plus one
/// enum member -- the same one-implementation-per-enum-member shape as <c>IEntityImporter</c>,
/// <c>IAlertContentBuilder</c> and <c>IGlPostingRule&lt;T&gt;</c>.</para>
/// </summary>
public enum ExportCategory
{
    Products,
    Contacts,
    ChartOfAccounts,
    LedgerTransactions,
    StockMovements,
}

/// <summary>
/// <para>Narrower than <c>ImportJobStatus</c> by one distinction, and deliberately so. An import's
/// defining state is <i>partial success</i> -- 997 rows created, 3 rejected, and the job is
/// Completed. An export has no per-row outcome to be partial about: either the workbook was
/// produced or it was not. So <see cref="Failed"/> here means simply "no artifact exists", with the
/// reason recorded on the job.</para>
///
/// <para>What an export <i>can</i> be is <b>truncated</b> -- a category with more rows than
/// <c>ExportLimits.MaxRowsPerCategory</c> is cut off at the cap. That is not a status: the file is
/// complete and downloadable, so the job is <see cref="Completed"/> and the truncation is disclosed
/// on the job row, on the workbook's own Summary sheet, and in the completion email. Hiding it in a
/// status nobody reads would be the dishonest option.</para>
/// </summary>
public enum ExportJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}
