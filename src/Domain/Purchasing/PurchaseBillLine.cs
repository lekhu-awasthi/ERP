using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Purchasing;

/// <summary>Child line of PurchaseBill -- same shape as Sales.InvoiceLine (VatRate snapshot,
/// Amount/VatAmount computed at AddLine time), plus ExpenditureClassification (Annex 13, see that
/// enum's doc comment).</summary>
public sealed class PurchaseBillLine
{
    public Guid Id { get; private set; }
    public Guid PurchaseBillId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public VatRate VatRate { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }
    public ExpenditureClassification ExpenditureClassification { get; private set; }

    private PurchaseBillLine()
    {
    }

    internal static PurchaseBillLine Create(
        Guid purchaseBillId, Guid productId, decimal quantity, decimal rate, VatRate vatRate,
        ExpenditureClassification expenditureClassification)
    {
        var amount = quantity * rate;

        return new PurchaseBillLine
        {
            Id = Guid.NewGuid(),
            PurchaseBillId = purchaseBillId,
            ProductId = productId,
            Quantity = quantity,
            Rate = rate,
            VatRate = vatRate,
            Amount = amount,
            VatAmount = amount * vatRate.ToPercent(),
            ExpenditureClassification = expenditureClassification,
        };
    }
}
