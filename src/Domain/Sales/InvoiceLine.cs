using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Sales;

/// <summary>Child line of Invoice -- same shape as QuotationLine (VatRate snapshot,
/// Amount/VatAmount computed at AddLine time). See Invoice's doc comment for why the GL Sales
/// Account isn't stored here -- it's resolved at Approve time by the Application-layer handler,
/// not a Domain concern.</summary>
public sealed class InvoiceLine
{
    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public VatRate VatRate { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }

    /// <summary>Null until Invoice.Approve() actually consumes FIFO stock for this line (a
    /// Service line, or a Draft line, never gets one). Set once, from
    /// IStockLedgerService.ConsumeAsync's actual weighted-average result -- not recomputed later --
    /// so a CreditNote reversing this line can put stock back at the exact cost it left at, instead
    /// of guessing from whatever FIFO layers happen to exist at CreditNote-approval time.</summary>
    public decimal? CogsUnitCost { get; private set; }

    private InvoiceLine()
    {
    }

    internal static InvoiceLine Create(Guid invoiceId, Guid productId, decimal quantity, decimal rate, VatRate vatRate)
    {
        var amount = quantity * rate;

        return new InvoiceLine
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoiceId,
            ProductId = productId,
            Quantity = quantity,
            Rate = rate,
            VatRate = vatRate,
            Amount = amount,
            VatAmount = amount * vatRate.ToPercent(),
        };
    }

    /// <summary>Called once, from ApproveInvoiceCommandHandler right after
    /// IStockLedgerService.ConsumeAsync returns this line's actual weighted-average cost. Public
    /// (not internal) because its only real caller lives in the Application assembly -- see
    /// CLAUDE.md's internal-vs-public Domain-factory gotcha from Phase 7's StockLedgerEntry.Consume.</summary>
    public void RecordCogsUnitCost(decimal unitCost) => CogsUnitCost = unitCost;
}
