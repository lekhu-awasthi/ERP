using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Trees;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.BalanceSheet;

public sealed class BalanceSheetQueryHandler(IAppDbContext db, ITreeQuery<AccountGroup> treeQuery)
    : IRequestHandler<BalanceSheetQuery, BalanceSheetDto>
{
    public async Task<BalanceSheetDto> Handle(BalanceSheetQuery request, CancellationToken cancellationToken)
    {
        var cutoff = GlDateBoundary.EndOfDayUtc(request.AsOfDate);

        var accounts = await db.Accounts
            .Where(a => a.OrganizationId == request.OrganizationId && a.IsActive)
            .Select(a => new AccountProjection(a.Id, a.RootType, a.GroupId))
            .ToListAsync(cancellationToken);

        var glTotals = await (
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.OrganizationId == request.OrganizationId && entry.PostedAt <= cutoff
            group line by line.AccountId into g
            select new { AccountId = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToListAsync(cancellationToken);

        var glTotalsByAccount = glTotals.ToDictionary(x => x.AccountId, x => (x.Debit, x.Credit));

        decimal NetDebit(Guid accountId) =>
            glTotalsByAccount.TryGetValue(accountId, out var t) ? t.Debit - t.Credit : 0m;

        decimal NetCredit(Guid accountId) => -NetDebit(accountId);

        var totalAssets = accounts.Where(a => a.RootType == AccountRootType.Asset).Sum(a => NetDebit(a.Id));
        var totalLiabilities = accounts.Where(a => a.RootType == AccountRootType.Liability).Sum(a => NetCredit(a.Id));
        var totalEquityAccounts = accounts.Where(a => a.RootType == AccountRootType.Equity).Sum(a => NetCredit(a.Id));
        var totalIncome = accounts.Where(a => a.RootType == AccountRootType.Income).Sum(a => NetCredit(a.Id));
        var totalExpense = accounts.Where(a => a.RootType == AccountRootType.Expense).Sum(a => NetDebit(a.Id));
        var netIncome = totalIncome - totalExpense;
        var totalEquity = totalEquityAccounts + netIncome;

        var assetGroups = await BuildGroupBalancesAsync(
            request.OrganizationId, AccountRootType.Asset,
            accounts.Where(a => a.RootType == AccountRootType.Asset).ToList(), NetDebit, cancellationToken);
        var liabilityGroups = await BuildGroupBalancesAsync(
            request.OrganizationId, AccountRootType.Liability,
            accounts.Where(a => a.RootType == AccountRootType.Liability).ToList(), NetCredit, cancellationToken);
        var equityGroups = await BuildGroupBalancesAsync(
            request.OrganizationId, AccountRootType.Equity,
            accounts.Where(a => a.RootType == AccountRootType.Equity).ToList(), NetCredit, cancellationToken);
        equityGroups = [.. equityGroups, new AccountGroupBalanceDto(Guid.Empty, "Net Income (Current Period)", netIncome)];

        return new BalanceSheetDto(
            request.AsOfDate, assetGroups, liabilityGroups, equityGroups,
            netIncome, totalAssets, totalLiabilities, totalEquity);
    }

    private async Task<IReadOnlyList<AccountGroupBalanceDto>> BuildGroupBalancesAsync(
        Guid organizationId,
        AccountRootType rootType,
        IReadOnlyList<AccountProjection> accountsOfRootType,
        Func<Guid, decimal> naturalSideBalance,
        CancellationToken cancellationToken)
    {
        var topLevelGroups = await db.AccountGroups
            .Where(g => g.OrganizationId == organizationId && g.RootType == rootType && g.ParentGroupId == null)
            .Select(g => new { g.Id, g.Name })
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken);

        var result = new List<AccountGroupBalanceDto>();
        foreach (var group in topLevelGroups)
        {
            var subtreeIds = await treeQuery.GetSubtreeIdsAsync(organizationId, group.Id, cancellationToken);
            var subtreeSet = subtreeIds.ToHashSet();
            var balance = accountsOfRootType.Where(a => subtreeSet.Contains(a.GroupId)).Sum(a => naturalSideBalance(a.Id));
            result.Add(new AccountGroupBalanceDto(group.Id, group.Name, balance));
        }

        return result;
    }

    private sealed record AccountProjection(Guid Id, AccountRootType RootType, Guid GroupId);
}
