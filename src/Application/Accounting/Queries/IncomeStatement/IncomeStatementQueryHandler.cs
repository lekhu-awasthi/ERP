using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.IncomeStatement;

public sealed class IncomeStatementQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<IncomeStatementQuery, IncomeStatementDto>
{
    public async Task<IncomeStatementDto> Handle(IncomeStatementQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null (unrestricted) unless the tenant has turned
        // the toggle on AND this caller's role carries location-specific grants, so no existing
        // tenant's figures change.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);
        var entries = GlEntryLocations.ForReport(db, request.OrganizationId, request.LocationId, reportLocations);

        var accounts = await db.Accounts
            .Where(a => a.OrganizationId == request.OrganizationId
                && (a.RootType == AccountRootType.Income || a.RootType == AccountRootType.Expense))
            .Select(a => new { a.Id, a.Code, a.Name, a.RootType })
            .ToListAsync(cancellationToken);

        var movement = await MovementAsync(entries, request.FromDate, request.ToDate, cancellationToken);

        var comparePeriod = request.Compare
            ? ComparePeriod.SameLengthPrior(request.FromDate, request.ToDate)
            : ((DateOnly FromDate, DateOnly ToDate)?)null;
        var compareMovement = comparePeriod is { } period
            ? await MovementAsync(entries, period.FromDate, period.ToDate, cancellationToken)
            : null;

        // Only accounts with actual movement -- this is a period-scoped P&L, not a full Chart of
        // Accounts listing (unlike TrialBalanceQuery's "every active Account"). With Compare on
        // that means the union of both windows; see the query's own doc comment for why.
        bool HasMovement(Guid accountId) =>
            movement.ContainsKey(accountId) || (compareMovement?.ContainsKey(accountId) ?? false);

        IReadOnlyList<IncomeStatementRowDto> Rows(AccountRootType rootType, Func<(decimal Debit, decimal Credit), decimal> sign) =>
        [
            .. accounts
                .Where(a => a.RootType == rootType && HasMovement(a.Id))
                .Select(a => new IncomeStatementRowDto(
                    a.Id, a.Code, a.Name, a.RootType,
                    Amount: sign(movement.GetValueOrDefault(a.Id)),
                    CompareAmount: compareMovement is null ? null : sign(compareMovement.GetValueOrDefault(a.Id))))
                .OrderBy(r => r.AccountCode),
        ];

        var incomeRows = Rows(AccountRootType.Income, t => t.Credit - t.Debit);
        var expenseRows = Rows(AccountRootType.Expense, t => t.Debit - t.Credit);

        return new IncomeStatementDto(
            request.FromDate,
            request.ToDate,
            incomeRows,
            expenseRows,
            TotalIncome: incomeRows.Sum(r => r.Amount),
            TotalExpense: expenseRows.Sum(r => r.Amount),
            CompareFromDate: comparePeriod?.FromDate,
            CompareToDate: comparePeriod?.ToDate,
            CompareTotalIncome: compareMovement is null ? null : incomeRows.Sum(r => r.CompareAmount ?? 0m),
            CompareTotalExpense: compareMovement is null ? null : expenseRows.Sum(r => r.CompareAmount ?? 0m));
    }

    /// <summary>Debit/Credit movement per account over one window -- run twice when Compare is on.</summary>
    private async Task<Dictionary<Guid, (decimal Debit, decimal Credit)>> MovementAsync(
        IQueryable<GlJournalEntry> entries, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var fromUtc = GlDateBoundary.StartOfDayUtc(fromDate);
        var toUtc = GlDateBoundary.EndOfDayUtc(toDate);

        var glTotals = await (
            from line in db.GlLines
            join entry in entries on line.GlJournalEntryId equals entry.Id
            where entry.PostedAt >= fromUtc && entry.PostedAt <= toUtc
            group line by line.AccountId into g
            select new { AccountId = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToListAsync(cancellationToken);

        return glTotals.ToDictionary(x => x.AccountId, x => (x.Debit, x.Credit));
    }
}
