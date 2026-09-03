using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Trade.Queries.TradeByItemMonthly;

/// <summary>
/// Sales By Item (Monthly) and Purchase By Item (Monthly) -- one handler discriminated by
/// <see cref="TradeSide"/>, both read live on 2026-09-03. Same BS fiscal-year crosstab as
/// <c>TradeByContactMonthlyQuery</c> (see <see cref="TradeMonthlyCrosstab"/>), grouped by product
/// instead of contact, and with <b>no filter at all beyond the fiscal year</b> -- neither live
/// screen offers one.
///
/// <para>The measure is Net Sales / Net Purchase, matching the contact crosstab and the live
/// figures.</para>
/// </summary>
public sealed record TradeByItemMonthlyQuery(
    Guid OrganizationId,
    TradeSide Side,
    int FiscalYear,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<TradeByItemMonthlyDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey =>
        Side == TradeSide.Sales ? PermissionKeys.SalesByItemMonthlyView : PermissionKeys.PurchaseByItemMonthlyView;
}

public sealed record TradeByItemMonthlyRowDto(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    IReadOnlyList<decimal> Monthly,
    IReadOnlyList<decimal> Quarters,
    decimal Total);

public sealed record TradeByItemMonthlyDto(
    TradeSide Side,
    int FiscalYear,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<TradeMonthlyColumnDto> Columns,
    IReadOnlyList<TradeByItemMonthlyRowDto> Rows,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<decimal> TotalMonthly,
    IReadOnlyList<decimal> TotalQuarters,
    decimal Total);
