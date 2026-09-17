using ErpApp.Domain.Common;
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
    public decimal DiscountPct { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }

    /// <summary>Phase 52 -- the unit this line was <b>entered</b> in (null means the product's own
    /// primary unit) and how many primary units one of them is worth, frozen when the line was
    /// written. See <c>InvoiceLine</c> for the full reasoning and <see cref="UnitConversion"/> for
    /// the live evidence behind freezing the factor rather than re-reading it.</summary>
    public Guid? UnitId { get; private set; }

    /// <inheritdoc cref="UnitId"/>
    public decimal ConversionFactor { get; private set; }

    /// <summary>This line's quantity in the product's primary unit -- the only quantity the stock
    /// ledger and the GL accept.</summary>
    public PrimaryQuantity PrimaryQuantity => PrimaryQuantity.FromEntered(Quantity, ConversionFactor);

    private SalesOrderLine()
    {
    }

    /// <summary>See InvoiceLine.Create's doc comment -- Amount/VatAmount fold in both line and
    /// header DiscountPct.</summary>
    internal static SalesOrderLine Create(
        Guid salesOrderId, Guid productId, decimal quantity, decimal rate, VatRate vatRate,
        decimal discountPct, decimal headerDiscountPct, Guid? unitId, decimal conversionFactor)
    {
        var grossAmount = quantity * rate;
        var netAfterLineDiscount = grossAmount * (1 - discountPct / 100m);
        var amount = netAfterLineDiscount * (1 - headerDiscountPct / 100m);

        return new SalesOrderLine
        {
            Id = Guid.NewGuid(),
            SalesOrderId = salesOrderId,
            ProductId = productId,
            UnitId = unitId,
            ConversionFactor = UnitConversion.Validate(conversionFactor),
            Quantity = quantity,
            Rate = rate,
            VatRate = vatRate,
            DiscountPct = discountPct,
            Amount = amount,
            VatAmount = amount * vatRate.ToPercent(),
        };
    }
}
