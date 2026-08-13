using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.IncomeStatement;

public sealed class IncomeStatementQueryHandler(IAppDbContext db) : IRequestHandler<IncomeStatementQuery, IncomeStatementDto>
{
    public async Task<IncomeStatementDto> Handle(IncomeStatementQuery request, CancellationToken cancellationToken)
    {
        var fromUtc = GlDateBoundary.StartOfDayUtc(request.FromDate);
        var toUtc = GlDateBoundary.EndOfDayUtc(request.ToDate);

        var accounts = await db.Accounts
            .Where(a => a.OrganizationId == request.OrganizationId
                && (a.RootType == AccountRootType.Income || a.RootType == AccountRootType.Expense))
            .Select(a => new { a.Id, a.Code, a.Name, a.RootType })
            .ToListAsync(cancellationToken);

        var glTotals = await (
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.OrganizationId == request.OrganizationId && entry.PostedAt >= fromUtc && entry.PostedAt <= toUtc
            group line by line.AccountId into g
            select new { AccountId = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToListAsync(cancellationToken);

        var glTotalsByAccount = glTotals.ToDictionary(x => x.AccountId, x => (x.Debit, x.Credit));

        // Only accounts with actual movement in the period -- this is a period-scoped P&L, not a
        // full Chart of Accounts listing (unlike TrialBalanceQuery's "every active Account").
        var incomeRows = accounts
            .Where(a => a.RootType == AccountRootType.Income && glTotalsByAccount.ContainsKey(a.Id))
            .Select(a =>
            {
                var (debit, credit) = glTotalsByAccount[a.Id];
                return new IncomeStatementRowDto(a.Id, a.Code, a.Name, a.RootType, Amount: credit - debit);
            })
            .OrderBy(r => r.AccountCode)
            .ToList();

        var expenseRows = accounts
            .Where(a => a.RootType == AccountRootType.Expense && glTotalsByAccount.ContainsKey(a.Id))
            .Select(a =>
            {
                var (debit, credit) = glTotalsByAccount[a.Id];
                return new IncomeStatementRowDto(a.Id, a.Code, a.Name, a.RootType, Amount: debit - credit);
            })
            .OrderBy(r => r.AccountCode)
            .ToList();

        return new IncomeStatementDto(
            request.FromDate, request.ToDate, incomeRows, expenseRows,
            TotalIncome: incomeRows.Sum(r => r.Amount), TotalExpense: expenseRows.Sum(r => r.Amount));
    }
}
