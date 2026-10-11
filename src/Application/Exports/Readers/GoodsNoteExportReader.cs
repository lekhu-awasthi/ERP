using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Exports;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>
/// Phase 80 -- one row per Delivery Note or Goods Received Note <i>line</i>. These are phase 58's physical-movement
/// documents. They write only the second, quantity-only stock ledger and post nothing, so Stock Movements (the FIFO
/// ledger) never sees them and nothing else in the export did.
///
/// <para><b>Quantities as written, with a Direction column</b>, the way Stock Movements prints its rows: a delivery is
/// <i>Out</i>, a receipt <i>In</i>. Negating a delivery would make its rate and amount read as a refund. The figures
/// are the note's own (a note carries rates and VAT as its form does), not money anyone owes: the Invoice or Purchase
/// Bill does that.</para>
///
/// <para>Then the note's own fields: its <b>Warehouse</b>; <b>Against Order</b>, the Sales Order or Purchase Order it
/// was raised from (phase 6's referrer); <b>Tracking No</b>; and, on a delivery, its <b>Expected Delivery Date</b> and
/// <b>Shipping Address</b>. Then phase 78's Unit, Exchange Rate and Location.</para>
/// </summary>
public sealed class GoodsNoteExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.GoodsNotes;

    public string SheetName => "Goods Notes";

    /// <summary>Filtered on the note's Date, the date this sheet shows.</summary>
    public bool IsDateFiltered => true;

    /// <summary>Every money figure a goods note stores that this sheet carries; <c>GoodsNoteExportTests</c> holds the
    /// four types to it.</summary>
    public static readonly IReadOnlySet<(Type Type, string Property)> ExportedMoney = new HashSet<(Type, string)>
    {
        (typeof(DeliveryNoteLine), nameof(DeliveryNoteLine.Quantity)),
        (typeof(DeliveryNoteLine), nameof(DeliveryNoteLine.Rate)),
        (typeof(DeliveryNoteLine), nameof(DeliveryNoteLine.DiscountPct)),
        (typeof(DeliveryNoteLine), nameof(DeliveryNoteLine.Amount)),
        (typeof(DeliveryNoteLine), nameof(DeliveryNoteLine.VatAmount)),
        (typeof(GoodsReceivedNoteLine), nameof(GoodsReceivedNoteLine.Quantity)),
        (typeof(GoodsReceivedNoteLine), nameof(GoodsReceivedNoteLine.Rate)),
        (typeof(GoodsReceivedNoteLine), nameof(GoodsReceivedNoteLine.DiscountPct)),
        (typeof(GoodsReceivedNoteLine), nameof(GoodsReceivedNoteLine.Amount)),
        (typeof(GoodsReceivedNoteLine), nameof(GoodsReceivedNoteLine.VatAmount)),
        (typeof(DeliveryNote), nameof(DeliveryNote.DiscountPct)),
        (typeof(DeliveryNote), nameof(DeliveryNote.ExchangeRate)),
        (typeof(GoodsReceivedNote), nameof(GoodsReceivedNote.DiscountPct)),
        (typeof(GoodsReceivedNote), nameof(GoodsReceivedNote.ExchangeRate)),
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
        "Direction",
        "Warehouse",
        "Against Order",
        "Tracking No",
        "Expected Delivery Date",
        "Shipping Address",
        "Document Discount %",
        "Unit",
        "Exchange Rate",
        "Location",
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, ExportDateRange range, CancellationToken cancellationToken)
    {
        var deliveryLines =
            from line in db.DeliveryNoteLines
            join doc in db.DeliveryNotes on line.DeliveryNoteId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on line.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            join order in db.SalesOrders
                on (doc.ReferrerType == DocumentType.SalesOrder ? doc.ReferrerId : null) equals (Guid?)order.Id into orders
            from order in orders.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new GoodsNoteRow(
                "Delivery Note", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                product == null ? null : product.Code, product == null ? null : product.Name,
                line.Quantity, line.Rate, line.DiscountPct, line.Amount, line.VatAmount,
                StockMovementDirection.Out, doc.WarehouseId, order == null ? null : order.Code, doc.TrackingNo,
                doc.ExpectedDeliveryDate, doc.ShippingAddress, doc.DiscountPct,
                line.UnitId ?? (product == null ? null : (Guid?)product.PrimaryUnitId), doc.ExchangeRate, doc.LocationId);

        var receiptLines =
            from line in db.GoodsReceivedNoteLines
            join doc in db.GoodsReceivedNotes on line.GoodsReceivedNoteId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join product in db.Products on line.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            join order in db.PurchaseOrders
                on (doc.ReferrerType == DocumentType.PurchaseOrder ? doc.ReferrerId : null) equals (Guid?)order.Id into orders
            from order in orders.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new GoodsNoteRow(
                "Goods Received Note", doc.Code, doc.Date, doc.Status.ToString(),
                contact == null ? null : contact.Name, doc.Reference, doc.CurrencyCode,
                product == null ? null : product.Code, product == null ? null : product.Name,
                line.Quantity, line.Rate, line.DiscountPct, line.Amount, line.VatAmount,
                StockMovementDirection.In, doc.WarehouseId, order == null ? null : order.Code, doc.TrackingNo,
                null, null, doc.DiscountPct,
                line.UnitId ?? (product == null ? null : (Guid?)product.PrimaryUnitId), doc.ExchangeRate, doc.LocationId);

        var totalRowCount = 0;
        var merged = new List<GoodsNoteRow>();

        foreach (var query in new[] { deliveryLines, receiptLines })
        {
            totalRowCount += await query.CountAsync(cancellationToken);
            merged.AddRange(await query.Take(maxRows).ToListAsync(cancellationToken));
        }

        var names = new Names(
            await ExportLookups.UnitsAsync(db, organizationId, cancellationToken),
            await ExportLookups.LocationsAsync(db, organizationId, cancellationToken),
            await ExportLookups.WarehousesAsync(db, organizationId, cancellationToken));

        var rows = merged
            .OrderBy(r => r.Date)
            .ThenBy(r => r.DocumentType, StringComparer.Ordinal)
            .ThenBy(r => r.DocumentCode, StringComparer.Ordinal)
            .Take(maxRows)
            .Select(r => r.ToCells(names))
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }

    private sealed record Names(
        IReadOnlyDictionary<Guid, string> Units,
        IReadOnlyDictionary<Guid, string> Locations,
        IReadOnlyDictionary<Guid, string> Warehouses);

    private sealed record GoodsNoteRow(
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
        StockMovementDirection Direction,
        Guid WarehouseId,
        string? AgainstOrder,
        string? TrackingNo,
        DateOnly? ExpectedDeliveryDate,
        string? ShippingAddress,
        decimal DocumentDiscountPct,
        Guid? UnitId,
        decimal ExchangeRate,
        Guid? LocationId)
    {
        public object?[] ToCells(Names names) =>
        [
            .. new DocumentLineRow(
                DocumentType, DocumentCode, Date, Status, PartyName, Reference, CurrencyCode, ProductCode,
                ProductName, Quantity, Rate, DiscountPct, Amount, VatAmount).ToCells(),
            Direction.ToString(),
            names.Warehouses.NameOf(WarehouseId),
            AgainstOrder,
            TrackingNo,
            ExpectedDeliveryDate is { } expected ? ExportCell.LocalDate(expected) : null,
            ShippingAddress,
            DocumentDiscountPct,
            names.Units.NameOf(UnitId),
            ExchangeRate,
            names.Locations.NameOf(LocationId),
        ];
    }
}
