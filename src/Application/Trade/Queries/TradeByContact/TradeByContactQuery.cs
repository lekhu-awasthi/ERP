using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Trade.Queries.TradeByContact;

/// <summary>
/// Sales By Customer and Purchase By Supplier -- one handler, discriminated by
/// <see cref="TradeSide"/>. Both were read live on 2026-09-03: filters <i>Period</i> and
/// <i>Contact Group</i>; columns <i>Contact</i>, <i>Contact Group</i>, <i>Amount</i>,
/// <i>Discount</i>, <i>Net Sales</i>/<i>Net Purchase</i>, <i>Vat Amount</i>, <i>Total Amount</i>;
/// a footer <b>Total</b> over all five money columns.
///
/// <para>The live contact cell carries no code here, unlike Invoice Age's -- the DTO returns the
/// code anyway so the <c>.xlsx</c> can identify a row that two contacts share a name for, and the
/// screen renders what the live screen renders.</para>
/// </summary>
public sealed record TradeByContactQuery(
    Guid OrganizationId,
    TradeSide Side,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ContactGroupId = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<TradeByContactDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey =>
        Side == TradeSide.Sales ? PermissionKeys.SalesByCustomerView : PermissionKeys.PurchaseBySupplierView;
}

public sealed record TradeByContactRowDto(
    Guid ContactId,
    string ContactCode,
    string ContactName,
    string? ContactGroupName,
    decimal Amount,
    decimal Discount,
    decimal NetAmount,
    decimal VatAmount,
    decimal TotalAmount);

/// <summary>Total* fields span every filtered row, not just the current page (phase-16c).</summary>
public sealed record TradeByContactDto(
    TradeSide Side,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<TradeByContactRowDto> Rows,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalAmount,
    decimal TotalDiscount,
    decimal TotalNetAmount,
    decimal TotalVatAmount,
    decimal TotalTotalAmount);
