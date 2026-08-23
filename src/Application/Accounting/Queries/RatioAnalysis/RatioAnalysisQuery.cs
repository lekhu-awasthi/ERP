using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.RatioAnalysis;

/// <summary>
/// Phase 19 decision #6 -- erp-module-scan.md already fully specifies the ratio list, no live check
/// needed. Computed by calling BalanceSheetQueryHandler/IncomeStatementQueryHandler directly (their
/// internals, not a re-derivation from raw GL, per the kickoff's own instruction) for AsOfDate=ToDate
/// and the [FromDate,ToDate] period, plus TenantSettings' own DefaultAccountsReceivableId/
/// DefaultAccountsPayableId/DefaultInventoryAccountId (Phase 5/7) for the Current-Asset-shaped
/// figures Liquidity ratios need -- this codebase's Chart of Accounts has no Current/Non-current
/// AccountGroup classification, so "Current Assets"/"Current Liabilities" are approximated as
/// Receivables+Inventory+Cash&amp;Bank / Payables (the actual GL accounts this codebase tracks a
/// default for) rather than Total Assets/Total Liabilities -- see phase-19-status.md's known
/// limitations. Sales/CostOfSales reuse Product Profitability's own InvoiceLine.Amount/CogsUnitCost
/// computation (IncomeStatementQueryHandler has no separate Cost-of-Sales line -- see its own doc
/// comment, a flat Income/Expense split, not Direct/Indirect/Gross-Profit staged like the live
/// Tigg screen).
/// </summary>
public sealed record RatioAnalysisQuery(Guid OrganizationId, DateOnly FromDate, DateOnly ToDate)
    : IRequest<RatioAnalysisDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.RatioAnalysisView;
}

public sealed record RatioAnalysisDto(
    DateOnly FromDate,
    DateOnly ToDate,
    // Liquidity
    decimal CurrentRatio,
    decimal QuickRatio,
    decimal CashRatio,
    // Solvency
    decimal DebtToEquityRatio,
    decimal DebtRatio,
    // Efficiency
    decimal InventoryTurnover,
    decimal ReceivablesTurnover,
    decimal AssetTurnover,
    decimal ReceivableDays,
    decimal PayableDays,
    decimal InventoryHoldingPeriodDays,
    decimal CashConversionCycleDays,
    // Profitability
    decimal GrossProfitMarginPct,
    decimal NetProfitMarginPct,
    decimal ReturnOnAssetsPct,
    decimal ReturnOnEquityPct);
