using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Exports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>FR-2.8's "chart of accounts" category. Ordered by code, which is the order an
/// accountant expects a chart of accounts in.</summary>
public sealed class ChartOfAccountsExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.ChartOfAccounts;

    public string SheetName => "Chart of Accounts";

    public IReadOnlyList<string> Headers { get; } =
    [
        "Account Code",
        "Account Name",
        "Root Type",
        "Account Group",
        "Kind",
        "Bank",
        "Account Number",
        "Active",
        "Created At",
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, CancellationToken cancellationToken)
    {
        var query =
            from account in db.Accounts
            where account.OrganizationId == organizationId
            join accountGroup in db.AccountGroups on account.GroupId equals accountGroup.Id into groups
            from accountGroup in groups.DefaultIfEmpty()
            join bank in db.Banks on account.BankId equals bank.Id into banks
            from bank in banks.DefaultIfEmpty()
            orderby account.Code
            select new
            {
                account.Code,
                account.Name,
                account.RootType,
                GroupName = accountGroup == null ? null : accountGroup.Name,
                account.Kind,
                BankName = bank == null ? null : bank.Name,
                account.AccountNumber,
                account.IsActive,
                account.CreatedAt,
            };

        var totalRowCount = await query.CountAsync(cancellationToken);
        var page = await query.Take(maxRows).ToListAsync(cancellationToken);

        var rows = page
            .Select(a => new object?[]
            {
                a.Code,
                a.Name,
                a.RootType.ToString(),
                a.GroupName,
                a.Kind.ToString(),
                a.BankName,
                a.AccountNumber,
                a.IsActive,
                ExportCell.LocalTimestamp(a.CreatedAt),
            })
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }
}
