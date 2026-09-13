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
/// <para><b>Phase 38 added three, by a rule rather than by taste.</b> A category earns its place
/// when its rows <i>cannot be reconstructed from the five already here</i>. Ledger Transactions is
/// the posted General Ledger, so it carries what every document did to the accounts and nothing of
/// what the document says: no quantities, no unit rates, no per-line VAT, no line text. A tenant
/// taking their data out therefore had the accounting and not the trade. <see cref="SalesDocuments"/>,
/// <see cref="PurchaseDocuments"/> and <see cref="Payments"/> close exactly that gap and stop
/// there -- the rule refuses, for instance, a Stock Position category, which is
/// <see cref="StockMovements"/> added up.</para>
public enum ExportCategory
{
    Products,
    Contacts,
    ChartOfAccounts,
    LedgerTransactions,
    StockMovements,

    /// <summary>Phase 38 -- Invoice and Credit Note lines, the sales side of what the GL cannot say.</summary>
    SalesDocuments,

    /// <summary>Phase 38 -- Purchase Bill and Debit Note lines.</summary>
    PurchaseDocuments,

    /// <summary>Phase 38 -- money received and paid, both directions, with the contact it moved
    /// against. Not derivable from the ledger either: a payment's own document number, mode and
    /// cheque details live on the Payment, not on the entry it posted.</summary>
    Payments,
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
