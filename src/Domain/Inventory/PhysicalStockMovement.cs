using ErpApp.Domain.Common;

namespace ErpApp.Domain.Inventory;

/// <summary>
/// Phase 58 -- one row of the <b>physical</b> stock ledger that only a Delivery Note or a Goods
/// Received Note writes: goods leaving or entering a warehouse, recorded apart from the documents
/// that bill for them. See <see cref="StockBooks"/> for why the ledger exists and why the rest of it
/// is derived rather than stored.
///
/// <para><b>Quantity only.</b> No unit cost, no value, no FIFO layer, and nothing is posted to the
/// general ledger: the reference product posts nothing for either document (its Trial Balance after
/// a GRN, a DN and their bills held the bills and nothing else), so there is no account a value here
/// would ever have to agree with.</para>
///
/// <para><b>Append-only, in the <c>StockMovement</c> shape.</b> A void never deletes or edits a
/// row; it writes the mirror row against the same source document, so a dated balance taken before
/// the void still shows the goods and one taken after it does not. <see cref="Quantity"/> is always a
/// positive magnitude in the product's <b>primary</b> unit and <see cref="Direction"/> carries the
/// sign -- the same convention <c>StockMovement</c> uses, which is what lets one reader fold both
/// tables into one balance.</para>
/// </summary>
public sealed class PhysicalStockMovement
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public StockMovementDirection Direction { get; private set; }
    public decimal Quantity { get; private set; }
    public DocumentType SourceDocumentType { get; private set; }
    public Guid SourceDocumentId { get; private set; }
    public DateOnly TransactionDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The billing location of the source document, stamped at write time -- phase 35b's
    /// rule for an append-only fact row, and the column the Inventory Variance Report's Billing
    /// Location filter reads.</summary>
    public Guid? LocationId { get; private set; }

    private PhysicalStockMovement()
    {
    }

    public static PhysicalStockMovement Create(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        StockMovementDirection direction,
        PrimaryQuantity quantity,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        Guid? locationId)
    {
        if (quantity.Value <= 0)
        {
            throw new InvalidOperationException("A physical stock movement needs a positive quantity.");
        }

        if (!StockBooks.PhysicalOnly.Contains(sourceDocumentType))
        {
            // Every other stock-moving type reaches the physical ledger through its StockMovement
            // rows (StockBooks.Shared) or not at all (StockBooks.AccountingOnly). Writing one of them
            // here as well would count it twice.
            throw new InvalidOperationException(
                $"Only a Delivery Note or a Goods Received Note writes the physical ledger directly, not a {sourceDocumentType}.");
        }

        return new PhysicalStockMovement
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProductId = productId,
            WarehouseId = warehouseId,
            Direction = direction,
            Quantity = quantity.Value,
            SourceDocumentType = sourceDocumentType,
            SourceDocumentId = sourceDocumentId,
            TransactionDate = transactionDate,
            LocationId = locationId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>The row a void writes: this movement's exact mirror, against the same source
    /// document, dated <paramref name="transactionDate"/>.</summary>
    public PhysicalStockMovement Reverse(DateOnly transactionDate) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = OrganizationId,
        ProductId = ProductId,
        WarehouseId = WarehouseId,
        Direction = Direction == StockMovementDirection.In ? StockMovementDirection.Out : StockMovementDirection.In,
        Quantity = Quantity,
        SourceDocumentType = SourceDocumentType,
        SourceDocumentId = SourceDocumentId,
        TransactionDate = transactionDate,
        LocationId = LocationId,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
