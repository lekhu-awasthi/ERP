using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Purchasing;

/// <summary>Child line of PurchaseOrder -- same shape as Sales.QuotationLine (VatRate snapshot,
/// Amount/VatAmount computed at AddLine time).</summary>
public sealed class PurchaseOrderLine
{
    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public VatRate VatRate { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }

    private PurchaseOrderLine()
    {
    }

    internal static PurchaseOrderLine Create(Guid purchaseOrderId, Guid productId, decimal quantity, decimal rate, VatRate vatRate)
    {
        var amount = quantity * rate;

        return new PurchaseOrderLine
        {
            Id = Guid.NewGuid(),
            PurchaseOrderId = purchaseOrderId,
            ProductId = productId,
            Quantity = quantity,
            Rate = rate,
            VatRate = vatRate,
            Amount = amount,
            VatAmount = amount * vatRate.ToPercent(),
        };
    }
}
