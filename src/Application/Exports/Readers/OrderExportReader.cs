using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Exports;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>
/// Phase 80 -- one row per Quotation, Sales Order or Purchase Order <i>line</i>: what was offered or promised, which
/// no other sheet can say, because a commitment posts nothing and moves no stock.
///
/// <para><b>A sheet of its own, never rows on Sales or Purchase Documents.</b> Those two sheets sum to net sales and net
/// purchases (phase 38's signed quantities). An order on them would be counted again by the invoice or bill it became.
/// Here every figure is as written, positive, and the sum of a column is what was quoted or ordered, which is all a
/// commitment's sum can mean.</para>
///
/// <para>Phase 38's fourteen columns through <see cref="DocumentLineRow"/>, with <b>Party</b> for the customer or
/// supplier (one sheet holds both sides; Document Type says which). Then each order's own dates: a quotation's
/// <b>Valid Until</b>, a sales order's <b>Delivery Date</b>, and <b>Delivered / Received At</b>, the moment a Delivery
/// Note or GRN was raised against it. Then phase 78's <b>Unit</b>, <b>Exchange Rate</b> and <b>Location</b>. Drafts are
/// included and labelled by Status, as on every document sheet.</para>
///
/// <para>Three queries merged in memory (phase 77's shape): each is ordered and capped on its own, and the total is the
/// sum of the counts.</para>
/// </summary>
public sealed class OrderExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.Orders;

    public string SheetName => "Orders";

    /// <summary>Filtered on the order's own Date, the date this sheet shows.</summary>
    public bool IsDateFiltered => true;

    /// <summary>
    /// Every money figure an order stores that this sheet carries, by type and property. <c>OrderExportTests</c> holds
    /// every <c>decimal</c> on the six types to this list or a named reason, as phase 78 holds the purchase types.
    /// </summary>
    public static readonly IReadOnlySet<(Type Type, string Property)> ExportedMoney = new HashSet<(Type, string)>
    {
        (typeof(QuotationLine), nameof(QuotationLine.Quantity)),
        (typeof(QuotationLine), nameof(QuotationLine.Rate)),
        (typeof(QuotationLine), nameof(QuotationLine.DiscountPct)),
        (typeof(QuotationLine), nameof(QuotationLine.Amount)),
        (typeof(QuotationLine), nameof(QuotationLine.VatAmount)),
        (typeof(SalesOrderLine), nameof(SalesOrderLine.Quantity)),
        (typeof(SalesOrderLine), nameof(SalesOrderLine.Rate)),
        (typeof(SalesOrderLine), nameof(SalesOrderLine.DiscountPct)),
        (typeof(SalesOrderLine), nameof(SalesOrderLine.Amount)),
        (typeof(SalesOrderLine), nameof(SalesOrderLine.VatAmount)),
        (typeof(PurchaseOrderLine), nameof(PurchaseOrderLine.Quantity)),
        (typeof(PurchaseOrderLine), nameof(PurchaseOrderLine.Rate)),
        (typeof(PurchaseOrderLine), nameof(PurchaseOrderLine.DiscountPct)),
        (typeof(PurchaseOrderLine), nameof(PurchaseOrderLine.Amount)),
        (typeof(PurchaseOrderLine), nameof(PurchaseOrderLine.VatAmount)),
        (typeof(Quotation), nameof(Quotation.DiscountPct)),
        (typeof(Quotation), nameof(Quotation.ExchangeRate)),
        (typeof(SalesOrder), nameof(SalesOrder.DiscountPct)),
        (typeof(SalesOrder), nameof(SalesOrder.ExchangeRate)),
        (typeof(PurchaseOrder), nameof(PurchaseOrder.DiscountPct)),
        (typeof(PurchaseOrder), nameof(PurchaseOrder.ExchangeRate)),
    };

    public IReadOnlyList<string> Headers { get; } =
    [
        "Document Type",
        "Document No",
        "Date",
        "Status",
        "Party",
        "Reference",
        "Currency",
        "Product Code",
        "Product Name",
        "Quantity",
        "Rate",
        "Discount %",
        "Amount",
        "VAT Amount",
        "Document Discount %",
        "Valid Until",
        "Delivery Date",
        "Delivered / Received At",
        "Unit",
        "Exchange Rate",
        "Location",
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, ExportDateRange range, CancellationToken cancellationToken)
    {
        var quotationLines =
            from line in db.QuotationLines
            join doc in db.Quotations on line.QuotationId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on line.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new OrderRow(
                "Quotation", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                product == null ? null : product.Code, product == null ? null : product.Name,
                line.Quantity, line.Rate, line.DiscountPct, line.Amount, line.VatAmount,
                doc.DiscountPct, doc.ExpiryDate, null, null,
                line.UnitId ?? (product == null ? null : (Guid?)product.PrimaryUnitId), doc.ExchangeRate, doc.LocationId);

        var salesOrderLines =
            from line in db.SalesOrderLines
            join doc in db.SalesOrders on line.SalesOrderId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on line.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new OrderRow(
                "Sales Order", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                product == null ? null : product.Code, product == null ? null : product.Name,
                line.Quantity, line.Rate, line.DiscountPct, line.Amount, line.VatAmount,
                doc.DiscountPct, null, doc.DeliveryDate, doc.DeliveredAt,
                line.UnitId ?? (product == null ? null : (Guid?)product.PrimaryUnitId), doc.ExchangeRate, doc.LocationId);

        var purchaseOrderLines =
            from line in db.PurchaseOrderLines
            join doc in db.PurchaseOrders on line.PurchaseOrderId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on line.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new OrderRow(
                "Purchase Order", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                product == null ? null : product.Code, product == null ? null : product.Name,
                line.Quantity, line.Rate, line.DiscountPct, line.Amount, line.VatAmount,
                doc.DiscountPct, null, null, doc.ReceivedAt,
                line.UnitId ?? (product == null ? null : (Guid?)product.PrimaryUnitId), doc.ExchangeRate, doc.LocationId);

        var totalRowCount = 0;
        var merged = new List<OrderRow>();

        foreach (var query in new[] { quotationLines, salesOrderLines, purchaseOrderLines })
        {
            totalRowCount += await query.CountAsync(cancellationToken);
            merged.AddRange(await query.Take(maxRows).ToListAsync(cancellationToken));
        }

        var units = await ExportLookups.UnitsAsync(db, organizationId, cancellationToken);
        var locations = await ExportLookups.LocationsAsync(db, organizationId, cancellationToken);

        var rows = merged
            .OrderBy(r => r.Date)
            .ThenBy(r => r.DocumentType, StringComparer.Ordinal)
            .ThenBy(r => r.DocumentCode, StringComparer.Ordinal)
            .Take(maxRows)
            .Select(r => r.ToCells(units, locations))
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }

    /// <summary>One row of the sheet, as every query projects it: plain values and ids, named after the page is read
    /// (<see cref="ExportLookups"/>).</summary>
    private sealed record OrderRow(
        string DocumentType,
        string DocumentCode,
        DateOnly Date,
        string Status,
        string? PartyName,
        string? Reference,
        string CurrencyCode,
        string? ProductCode,
        string? ProductName,
        decimal Quantity,
        decimal Rate,
        decimal DiscountPct,
        decimal Amount,
        decimal VatAmount,
        decimal DocumentDiscountPct,
        DateOnly? ValidUntil,
        DateOnly? DeliveryDate,
        DateTimeOffset? FulfilledAt,
        Guid? UnitId,
        decimal ExchangeRate,
        Guid? LocationId)
    {
        public object?[] ToCells(IReadOnlyDictionary<Guid, string> units, IReadOnlyDictionary<Guid, string> locations) =>
        [
            .. new DocumentLineRow(
                DocumentType, DocumentCode, Date, Status, PartyName, Reference, CurrencyCode, ProductCode,
                ProductName, Quantity, Rate, DiscountPct, Amount, VatAmount).ToCells(),
            DocumentDiscountPct,
            ValidUntil is { } validUntil ? ExportCell.LocalDate(validUntil) : null,
            DeliveryDate is { } deliveryDate ? ExportCell.LocalDate(deliveryDate) : null,
            FulfilledAt is { } fulfilledAt ? ExportCell.LocalTimestamp(fulfilledAt) : null,
            units.NameOf(UnitId),
            ExchangeRate,
            locations.NameOf(LocationId),
        ];
    }
}
