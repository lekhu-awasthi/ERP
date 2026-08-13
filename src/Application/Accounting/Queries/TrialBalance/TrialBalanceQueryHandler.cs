using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.TrialBalance;

public sealed class TrialBalanceQueryHandler(IAppDbContext db) : IRequestHandler<TrialBalanceQuery, TrialBalanceDto>
{
    public async Task<TrialBalanceDto> Handle(TrialBalanceQuery request, CancellationToken cancellationToken)
    {
        var cutoff = GlDateBoundary.EndOfDayUtc(request.AsOfDate);

        var accounts = await db.Accounts
            .Where(a => a.OrganizationId == request.OrganizationId && a.IsActive)
            .Select(a => new { a.Id, a.Code, a.Name })
            .ToListAsync(cancellationToken);

        var glTotals = await (
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.OrganizationId == request.OrganizationId && entry.PostedAt <= cutoff
            group line by line.AccountId into g
            select new { AccountId = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToListAsync(cancellationToken);

        var glTotalsByAccount = glTotals.ToDictionary(x => x.AccountId, x => (x.Debit, x.Credit));

        var rows = accounts
            .Select(a =>
            {
                var (debit, credit) = glTotalsByAccount.TryGetValue(a.Id, out var totals) ? totals : (0m, 0m);
                var netDebit = debit - credit;
                return new TrialBalanceRowDto(
                    a.Id, a.Code, a.Name,
                    Debit: netDebit > 0 ? netDebit : 0m,
                    Credit: netDebit < 0 ? -netDebit : 0m);
            })
            .OrderBy(r => r.AccountCode)
            .ToList();

        return new TrialBalanceDto(request.AsOfDate, rows, rows.Sum(r => r.Debit), rows.Sum(r => r.Credit));
    }
}
