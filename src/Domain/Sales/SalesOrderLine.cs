using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Sales;

/// <summary>Same shape as QuotationLine -- see that type's doc comment.</summary>
public sealed class SalesOrderLine
{
    public Guid Id { get; private set; }
    public Guid SalesOrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public VatRate VatRate { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }

    private SalesOrderLine()
    {
    }

    internal static SalesOrderLine Create(Guid salesOrderId, Guid productId, decimal quantity, decimal rate, VatRate vatRate)
    {
        var amount = quantity * rate;

        return new SalesOrderLine
        {
            Id = Guid.NewGuid(),
            SalesOrderId = salesOrderId,
            ProductId = productId,
            Quantity = quantity,
            Rate = rate,
            VatRate = vatRate,
            Amount = amount,
            VatAmount = amount * vatRate.ToPercent(),
        };
    }
}
