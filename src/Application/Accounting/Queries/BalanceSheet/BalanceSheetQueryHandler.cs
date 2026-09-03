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
    /// <summary>The synthetic Equity plug row's id -- Guid.Empty, since it is not a real
    /// AccountGroup. Named because the Compare merge below keys on GroupId and has to match it.</summary>
    private static readonly Guid NetIncomePlugGroupId = Guid.Empty;

    public async Task<BalanceSheetDto> Handle(BalanceSheetQuery request, CancellationToken cancellationToken)
    {
        var accounts = await db.Accounts
            .Where(a => a.OrganizationId == request.OrganizationId && a.IsActive)
            .Select(a => new AccountProjection(a.Id, a.RootType, a.GroupId))
            .ToListAsync(cancellationToken);

        // The group hierarchy is resolved once and reused for both windows -- see the query's own
        // doc comment on why there is only ever one hierarchy to roll up through.
        var assetGroups = await LoadTopLevelGroupsAsync(request.OrganizationId, AccountRootType.Asset, cancellationToken);
        var liabilityGroups = await LoadTopLevelGroupsAsync(request.OrganizationId, AccountRootType.Liability, cancellationToken);
        var equityGroups = await LoadTopLevelGroupsAsync(request.OrganizationId, AccountRootType.Equity, cancellationToken);

        var main = Compute(
            accounts, assetGroups, liabilityGroups, equityGroups,
            await NetDebitsAsync(request.OrganizationId, request.AsOfDate, cancellationToken));

        var compareAsOfDate = request.Compare ? ComparePeriod.PriorYearAsOf(request.AsOfDate) : (DateOnly?)null;
        var compare = compareAsOfDate is { } compareDate
            ? Compute(
                accounts, assetGroups, liabilityGroups, equityGroups,
                await NetDebitsAsync(request.OrganizationId, compareDate, cancellationToken))
            : null;

        return new BalanceSheetDto(
            request.AsOfDate,
            Merge(main.AssetGroups, compare?.AssetGroups),
            Merge(main.LiabilityGroups, compare?.LiabilityGroups),
            Merge(main.EquityGroups, compare?.EquityGroups),
            main.NetIncome,
            main.TotalAssets,
            main.TotalLiabilities,
            main.TotalEquity,
            compareAsOfDate,
            compare?.NetIncome,
            compare?.TotalAssets,
            compare?.TotalLiabilities,
            compare?.TotalEquity);
    }

    /// <summary>Net debit (Debit minus Credit) per account at one cutoff -- run twice when Compare
    /// is on. Every balance below is derived from this one dictionary.</summary>
    private async Task<Dictionary<Guid, decimal>> NetDebitsAsync(
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

    private static Snapshot Compute(
        IReadOnlyList<AccountProjection> accounts,
        IReadOnlyList<GroupSubtree> assetGroups,
        IReadOnlyList<GroupSubtree> liabilityGroups,
        IReadOnlyList<GroupSubtree> equityGroups,
        Dictionary<Guid, decimal> netDebits)
    {
        decimal NetDebit(Guid accountId) => netDebits.GetValueOrDefault(accountId);
        decimal NetCredit(Guid accountId) => -NetDebit(accountId);

        var totalAssets = accounts.Where(a => a.RootType == AccountRootType.Asset).Sum(a => NetDebit(a.Id));
        var totalLiabilities = accounts.Where(a => a.RootType == AccountRootType.Liability).Sum(a => NetCredit(a.Id));
        var totalEquityAccounts = accounts.Where(a => a.RootType == AccountRootType.Equity).Sum(a => NetCredit(a.Id));
        var totalIncome = accounts.Where(a => a.RootType == AccountRootType.Income).Sum(a => NetCredit(a.Id));
        var totalExpense = accounts.Where(a => a.RootType == AccountRootType.Expense).Sum(a => NetDebit(a.Id));
        var netIncome = totalIncome - totalExpense;

        var assets = BuildGroupBalances(accounts, AccountRootType.Asset, assetGroups, NetDebit);
        var liabilities = BuildGroupBalances(accounts, AccountRootType.Liability, liabilityGroups, NetCredit);
        var equity = BuildGroupBalances(accounts, AccountRootType.Equity, equityGroups, NetCredit);
        equity = [.. equity, new AccountGroupBalanceDto(NetIncomePlugGroupId, "Net Income (Current Period)", netIncome)];

        return new Snapshot(
            assets, liabilities, equity, netIncome, totalAssets, totalLiabilities, totalEquityAccounts + netIncome);
    }

    /// <summary>
    /// Compare balances are folded into the main rows by GroupId. Both snapshots are built from the
    /// same group list in the same order, so this is a positional match in practice -- keying on
    /// GroupId anyway is what makes that an assertion rather than an assumption.
    /// </summary>
    private static IReadOnlyList<AccountGroupBalanceDto> Merge(
        IReadOnlyList<AccountGroupBalanceDto> main, IReadOnlyList<AccountGroupBalanceDto>? compare)
    {
        if (compare is null)
        {
            return main;
        }

        var compareByGroup = compare.ToDictionary(x => x.GroupId, x => x.Balance);
        return [.. main.Select(x => x with { CompareBalance = compareByGroup.GetValueOrDefault(x.GroupId) })];
    }

    private static IReadOnlyList<AccountGroupBalanceDto> BuildGroupBalances(
        IReadOnlyList<AccountProjection> accounts,
        AccountRootType rootType,
        IReadOnlyList<GroupSubtree> groups,
        Func<Guid, decimal> naturalSideBalance)
    {
        var accountsOfRootType = accounts.Where(a => a.RootType == rootType).ToList();
        return
        [
            .. groups.Select(group => new AccountGroupBalanceDto(
                group.Id,
                group.Name,
                accountsOfRootType
                    .Where(a => group.SubtreeGroupIds.Contains(a.GroupId))
                    .Sum(a => naturalSideBalance(a.Id)))),
        ];
    }

    private async Task<IReadOnlyList<GroupSubtree>> LoadTopLevelGroupsAsync(
        Guid organizationId, AccountRootType rootType, CancellationToken cancellationToken)
    {
        var topLevelGroups = await db.AccountGroups
            .Where(g => g.OrganizationId == organizationId && g.RootType == rootType && g.ParentGroupId == null)
            .Select(g => new { g.Id, g.Name })
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken);

        var result = new List<GroupSubtree>();
        foreach (var group in topLevelGroups)
        {
            var subtreeIds = await treeQuery.GetSubtreeIdsAsync(organizationId, group.Id, cancellationToken);
            result.Add(new GroupSubtree(group.Id, group.Name, subtreeIds.ToHashSet()));
        }

        return result;
    }

    private sealed record AccountProjection(Guid Id, AccountRootType RootType, Guid GroupId);

    private sealed record GroupSubtree(Guid Id, string Name, HashSet<Guid> SubtreeGroupIds);

    private sealed record Snapshot(
        IReadOnlyList<AccountGroupBalanceDto> AssetGroups,
        IReadOnlyList<AccountGroupBalanceDto> LiabilityGroups,
        IReadOnlyList<AccountGroupBalanceDto> EquityGroups,
        decimal NetIncome,
        decimal TotalAssets,
        decimal TotalLiabilities,
        decimal TotalEquity);
}
