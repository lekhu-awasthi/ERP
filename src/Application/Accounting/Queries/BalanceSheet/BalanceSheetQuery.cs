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
///
/// <para><b>Compare (Phase 26a, FR-9.1).</b> When Compare is set the handler runs a second GL
/// aggregation at <see cref="Reports.ComparePeriod.PriorYearAsOf"/> and folds it into this same
/// response as a CompareBalance per group plus compare totals. The group <i>structure</i> is not
/// re-derived for the comparison window -- AccountGroup has no effective-dating anywhere in this
/// codebase, so there is exactly one hierarchy and both windows roll up through it, which is also
/// what makes the two columns line up row-for-row by construction. Off by default; when off every
/// Compare* field is null rather than zero.</para>
/// </summary>
public sealed record BalanceSheetQuery(Guid OrganizationId, DateOnly AsOfDate, bool Compare = false)
    : IRequest<BalanceSheetDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BalanceSheetView;
}

public sealed record AccountGroupBalanceDto(
    Guid GroupId, string GroupName, decimal Balance, decimal? CompareBalance = null);

public sealed record BalanceSheetDto(
    DateOnly AsOfDate,
    IReadOnlyList<AccountGroupBalanceDto> AssetGroups,
    IReadOnlyList<AccountGroupBalanceDto> LiabilityGroups,
    IReadOnlyList<AccountGroupBalanceDto> EquityGroups,
    decimal NetIncome,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    DateOnly? CompareAsOfDate = null,
    decimal? CompareNetIncome = null,
    decimal? CompareTotalAssets = null,
    decimal? CompareTotalLiabilities = null,
    decimal? CompareTotalEquity = null)
{
    public bool IsBalanced => TotalAssets == TotalLiabilities + TotalEquity;
}
