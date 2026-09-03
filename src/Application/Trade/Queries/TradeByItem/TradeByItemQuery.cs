using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Trade.Queries.TradeByItem;

/// <summary>
/// Sales By Item and Purchase By Item -- one handler, discriminated by <see cref="TradeSide"/>.
/// Read live on 2026-09-03: columns <i>Product</i>, <i>Quantity</i>, <i>Amount</i>,
/// <i>Discount</i>, <i>Net Sales</i>/<i>Net Purchase</i>, <i>Vat Amount</i>, <i>Total Amount</i>,
/// with a footer <b>Total</b> over the five money columns and <b>Quantity deliberately left
/// blank</b> -- see <see cref="TradeByItemDto"/>.
///
/// <para><b><see cref="GroupBy"/> exists only on the Sales side of the live product</b>, where a
/// "Filter By item/category" control switches each row between one product and one product
/// category. Purchase By Item has no such control. Rather than build two nearly identical handlers
/// so that one of them can refuse an option, the parameter exists on both and the Purchase screen
/// simply never sends anything but <see cref="TradeItemGrouping.Item"/> -- the same shape as
/// serving two report identities from one query type.</para>
/// </summary>
public sealed record TradeByItemQuery(
    Guid OrganizationId,
    TradeSide Side,
    DateOnly FromDate,
    DateOnly ToDate,
    TradeItemGrouping GroupBy = TradeItemGrouping.Item,
    Guid? ProductCategoryId = null,
    Guid? ProductId = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<TradeByItemDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey =>
        Side == TradeSide.Sales ? PermissionKeys.SalesByItemView : PermissionKeys.PurchaseByItemView;
}

/// <summary>Whether a row is one product or one product category -- the live "Filter By
/// item/category" control's two options, in its own words.</summary>
public enum TradeItemGrouping
{
    Item,
    Category,
}

/// <summary>
/// <paramref name="Code"/> is null when the row is a category rather than a product --
/// <c>ProductCategory</c> has a name but no code. <c>Product.CategoryId</c> is required, so a
/// category-grouped run has no "uncategorised" bucket to render.
/// </summary>
public sealed record TradeByItemRowDto(
    Guid Id,
    string? Code,
    string Name,
    decimal Quantity,
    decimal Amount,
    decimal Discount,
    decimal NetAmount,
    decimal VatAmount,
    decimal TotalAmount);

/// <summary>
/// <b>There is no total quantity, on purpose.</b> The live footer totals the five money columns and
/// leaves Quantity blank, and it is right to: the rows are different products measured in different
/// units, so their quantities are not the same unit of account and adding them produces a number
/// with no meaning. That is phase-26a's own refusal, reached independently by the reference
/// product, and the DTO encodes it by simply not having the field -- a template cannot render a
/// total that does not exist.
/// </summary>
public sealed record TradeByItemDto(
    TradeSide Side,
    TradeItemGrouping GroupBy,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<TradeByItemRowDto> Rows,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalAmount,
    decimal TotalDiscount,
    decimal TotalNetAmount,
    decimal TotalVatAmount,
    decimal TotalTotalAmount);
