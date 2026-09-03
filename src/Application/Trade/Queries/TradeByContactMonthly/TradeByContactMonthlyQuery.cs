using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Trade.Queries.TradeByContactMonthly;

/// <summary>
/// Sales By Customer (Monthly) and Purchase By Supplier (Monthly) -- one handler discriminated by
/// <see cref="TradeSide"/>, both read live on 2026-09-03.
///
/// <para><b>Keyed by a BS fiscal year, not a date range.</b> <see cref="FiscalYear"/> names the
/// starting BS year, so 2083 means the fiscal year the live picker labels "2083 - 2084" and the
/// live subtitle calls "fiscal year 2083 / 2084" -- Shrawan 1, 2083 through the last day of Asar,
/// 2084. See <see cref="TradeMonthlyCrosstab"/> for the column layout and why the BS calendar had
/// to come to the server for this.</para>
///
/// <para><b>The measure is Net Sales / Net Purchase</b>, not Total Amount -- proved live by a
/// customer reading 45,000 in the crosstab against 45,000 Net Sales and 50,850 Total Amount in the
/// same period's Sales By Customer.</para>
///
/// <para><b>The sales side shows a PAN column and the purchase side does not</b> (live, and
/// asymmetric in the reference product). This DTO carries <c>Pan</c> on both, and each screen
/// renders what its live counterpart renders -- the field costs nothing on the wire and its absence
/// on one side is not a shape worth encoding twice. The permission split does treat the PAN column
/// as real: see <c>PermissionKeys</c>' phase-26b block.</para>
/// </summary>
public sealed record TradeByContactMonthlyQuery(
    Guid OrganizationId,
    TradeSide Side,
    int FiscalYear,
    Guid? ContactGroupId = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<TradeByContactMonthlyDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey =>
        Side == TradeSide.Sales
            ? PermissionKeys.SalesByCustomerMonthlyView
            : PermissionKeys.PurchaseBySupplierMonthlyView;
}

/// <summary><paramref name="Monthly"/> has one entry per column of
/// <see cref="TradeByContactMonthlyDto.Columns"/>, in the same order;
/// <paramref name="Quarters"/> has four.</summary>
public sealed record TradeByContactMonthlyRowDto(
    Guid ContactId,
    string ContactCode,
    string ContactName,
    string? Pan,
    string? ContactGroupName,
    IReadOnlyList<decimal> Monthly,
    IReadOnlyList<decimal> Quarters,
    decimal Total);

public sealed record TradeByContactMonthlyDto(
    TradeSide Side,
    int FiscalYear,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<TradeMonthlyColumnDto> Columns,
    IReadOnlyList<TradeByContactMonthlyRowDto> Rows,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<decimal> TotalMonthly,
    IReadOnlyList<decimal> TotalQuarters,
    decimal Total);
