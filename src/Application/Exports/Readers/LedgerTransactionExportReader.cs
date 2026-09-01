using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Exports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>
/// FR-2.8's "ledger transactions" category: the posted General Ledger, one row per
/// <c>GlLine</c>.
///
/// <para><b>The tenant filter is the thing to read twice.</b> <c>GlLine</c> has no
/// <c>OrganizationId</c> of its own -- it hangs off <c>GlJournalEntry</c>, which does -- so
/// isolation here depends entirely on the join, not on a column. That is the one place in this
/// feature where a hand-written filter could silently leak another tenant's ledger, and it is why
/// the phase's headline test asserts org B's rows are <i>absent</i> from org A's sheets rather than
/// merely asserting A's are present.</para>
///
/// <para><c>PostedAt</c> is stamped from the real clock at Approve time, never the document's own
/// business date (see <c>GlDateBoundary</c>) -- so this column is "when it hit the ledger", which is
/// exactly what a ledger export should say.</para>
/// </summary>
public sealed class LedgerTransactionExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.LedgerTransactions;

    public string SheetName => "Ledger Transactions";

    public IReadOnlyList<string> Headers { get; } =
    [
        "Posted At",
        "Source Document Type",
        "Source Document Id",
        "Account Code",
        "Account Name",
        "Debit",
        "Credit",
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, CancellationToken cancellationToken)
    {
        var query =
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.OrganizationId == organizationId
            join account in db.Accounts on line.AccountId equals account.Id into accounts
            from account in accounts.DefaultIfEmpty()
            orderby entry.PostedAt, entry.Id, line.Id
            select new
            {
                entry.PostedAt,
                entry.SourceDocumentType,
                entry.SourceDocumentId,
                AccountCode = account == null ? null : account.Code,
                AccountName = account == null ? null : account.Name,
                line.Debit,
                line.Credit,
            };

        var totalRowCount = await query.CountAsync(cancellationToken);
        var page = await query.Take(maxRows).ToListAsync(cancellationToken);

        var rows = page
            .Select(l => new object?[]
            {
                ExportCell.LocalTimestamp(l.PostedAt),
                l.SourceDocumentType.ToString(),
                l.SourceDocumentId.ToString(),
                l.AccountCode,
                l.AccountName,
                l.Debit,
                l.Credit,
            })
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }
}
