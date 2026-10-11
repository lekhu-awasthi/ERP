using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Exports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>
/// FR-2.8's "stock movements" category. <c>StockMovement</c> rather than <c>StockLedgerEntry</c>:
/// the movement table is the tenant-visible record of what went in and out, where the ledger
/// entries are FIFO cost layers -- an internal valuation mechanism whose <c>QuantityRemaining</c>
/// only makes sense to the costing engine that maintains it.
///
/// <para><b>Phase 80 -- Document No and Reference, appended</b>, as on Ledger Transactions and through the same resolver:
/// a Warehouse Transfer or an Inventory Adjustment is on no other sheet, and was named here only by its id.</para>
/// </summary>
public sealed class StockMovementExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.StockMovements;

    public string SheetName => "Stock Movements";

    /// <summary>Filtered on <c>TransactionDate</c>, the business date this sheet shows.</summary>
    public bool IsDateFiltered => true;

    public IReadOnlyList<string> Headers { get; } =
    [
        "Transaction Date",
        "Product Code",
        "Product Name",
        "Warehouse",
        "Direction",
        "Quantity",
        "Unit Cost",
        "Value",
        "Source Document Type",
        "Source Document Id",
        "Created At",
        "Document No",
        "Reference",
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, ExportDateRange range, CancellationToken cancellationToken)
    {
        var query =
            from movement in db.StockMovements
            where movement.OrganizationId == organizationId
                  && (range.From == null || movement.TransactionDate >= range.From)
                  && (range.To == null || movement.TransactionDate <= range.To)
            join product in db.Products on movement.ProductId equals product.Id into products
            from product in products.DefaultIfEmpty()
            join warehouse in db.Warehouses on movement.WarehouseId equals warehouse.Id into warehouses
            from warehouse in warehouses.DefaultIfEmpty()
            orderby movement.TransactionDate, movement.CreatedAt, movement.Id
            select new
            {
                movement.TransactionDate,
                ProductCode = product == null ? null : product.Code,
                ProductName = product == null ? null : product.Name,
                WarehouseName = warehouse == null ? null : warehouse.Name,
                movement.Direction,
                movement.Quantity,
                movement.UnitCost,
                movement.SourceDocumentType,
                movement.SourceDocumentId,
                movement.CreatedAt,
            };

        var totalRowCount = await query.CountAsync(cancellationToken);
        var page = await query.Take(maxRows).ToListAsync(cancellationToken);
        var documents = await GlSourceDocumentResolver.LoadEveryAsync(
            db, organizationId, page.Select(m => m.SourceDocumentType).ToHashSet(), cancellationToken);

        var rows = page
            .Select(m => new object?[]
            {
                m.TransactionDate,
                m.ProductCode,
                m.ProductName,
                m.WarehouseName,
                m.Direction.ToString(),
                m.Quantity,
                m.UnitCost,
                m.Quantity * m.UnitCost,
                m.SourceDocumentType.ToString(),
                m.SourceDocumentId.ToString(),
                ExportCell.LocalTimestamp(m.CreatedAt),
                documents.For(m.SourceDocumentType, m.SourceDocumentId)?.Code,
                documents.For(m.SourceDocumentType, m.SourceDocumentId)?.Reference,
            })
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }
}
