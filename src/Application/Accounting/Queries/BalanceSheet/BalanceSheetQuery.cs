using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.BalanceSheet;

/// <summary>
/// Asset/Liability/Equity accounts as of AsOfDate (end of day UTC, see GlDateBoundary), each
/// section grouped by top-level AccountGroup with a full-subtree rollup (ITreeQuery&lt;AccountGroup&gt;,
/// architecture-spec.md §5) so a group's balance includes every descendant subgroup's Accounts.
/// Equity carries a synthetic NetIncome "plug" row (cumulative Income-minus-Expense as of
/// AsOfDate) since there's no period-close/retained-earnings posting anywhere in this codebase --
/// see phase-8a-status.md's scope-decision section. TotalAssets/TotalLiabilities/TotalEquity are
/// computed independently of the group-rollup breakdown (straight from Account.RootType, the same
/// field GlPostingRule-adjacent code already treats as authoritative) so IsBalanced holds even if
/// a group's own RootType tagging is inconsistent with its Accounts' -- see the handler.
/// </summary>
public sealed record BalanceSheetQuery(Guid OrganizationId, DateOnly AsOfDate)
    : IRequest<BalanceSheetDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BalanceSheetView;
}

public sealed record AccountGroupBalanceDto(Guid GroupId, string GroupName, decimal Balance);

public sealed record BalanceSheetDto(
    DateOnly AsOfDate,
    IReadOnlyList<AccountGroupBalanceDto> AssetGroups,
    IReadOnlyList<AccountGroupBalanceDto> LiabilityGroups,
    IReadOnlyList<AccountGroupBalanceDto> EquityGroups,
    decimal NetIncome,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity)
{
    public bool IsBalanced => TotalAssets == TotalLiabilities + TotalEquity;
}
