using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.TrialBalance;

public sealed class TrialBalanceQueryHandler(IAppDbContext db) : IRequestHandler<TrialBalanceQuery, TrialBalanceDto>
{
    public async Task<TrialBalanceDto> Handle(TrialBalanceQuery request, CancellationToken cancellationToken)
    {
        var accounts = await db.Accounts
            .Where(a => a.OrganizationId == request.OrganizationId && a.IsActive)
            .Select(a => new { a.Id, a.Code, a.Name })
            .ToListAsync(cancellationToken);

        var netByAccount = await NetBalancesAsync(request.OrganizationId, request.AsOfDate, cancellationToken);

        var compareAsOfDate = request.Compare ? ComparePeriod.PriorYearAsOf(request.AsOfDate) : (DateOnly?)null;
        var compareNetByAccount = compareAsOfDate is { } compareDate
            ? await NetBalancesAsync(request.OrganizationId, compareDate, cancellationToken)
            : null;

        var rows = accounts
            .Select(a =>
            {
                var (debit, credit) = Sides(netByAccount.GetValueOrDefault(a.Id));
                var (compareDebit, compareCredit) = compareNetByAccount is null
                    ? (null, null)
                    : NullableSides(compareNetByAccount.GetValueOrDefault(a.Id));
                return new TrialBalanceRowDto(a.Id, a.Code, a.Name, debit, credit, compareDebit, compareCredit);
            })
            .OrderBy(r => r.AccountCode)
            .ToList();

        return new TrialBalanceDto(
            request.AsOfDate,
            rows,
            rows.Sum(r => r.Debit),
            rows.Sum(r => r.Credit),
            compareAsOfDate,
            compareNetByAccount is null ? null : rows.Sum(r => r.CompareDebit ?? 0m),
            compareNetByAccount is null ? null : rows.Sum(r => r.CompareCredit ?? 0m));
    }

    /// <summary>One GL aggregation at one cutoff -- run twice when Compare is on (see
    /// ComparePeriod). Returns net debit (positive) / net credit (negative) per account.</summary>
    private async Task<Dictionary<Guid, decimal>> NetBalancesAsync(
        Guid organizationId, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        var cutoff = GlDateBoundary.EndOfDayUtc(asOfDate);

        var glTotals = await (
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.OrganizationId == organizationId && entry.PostedAt <= cutoff
            group line by line.AccountId into g
            select new { AccountId = g.Key, Net = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) })
            .ToListAsync(cancellationToken);

        return glTotals.ToDictionary(x => x.AccountId, x => x.Net);
    }

    private static (decimal Debit, decimal Credit) Sides(decimal net) =>
        (net > 0 ? net : 0m, net < 0 ? -net : 0m);

    private static (decimal? Debit, decimal? Credit) NullableSides(decimal net)
    {
        var (debit, credit) = Sides(net);
        return (debit, credit);
    }
}
