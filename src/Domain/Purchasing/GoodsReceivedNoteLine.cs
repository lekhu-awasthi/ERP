using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.Purchasing;

/// <summary>Phase 58 -- child line of <see cref="GoodsReceivedNote"/>, the <see cref="PurchaseOrderLine"/>
/// shape exactly. It carries money (rate, discount, VAT) because the reference product's GRN line
/// does, and none of that money is posted anywhere -- only <see cref="PrimaryQuantity"/> reaches a
/// ledger.</summary>
public sealed class GoodsReceivedNoteLine
{
    public Guid Id { get; private set; }
    public Guid GoodsReceivedNoteId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public VatRate VatRate { get; private set; }
    public decimal DiscountPct { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }

    /// <summary>Phase 52's unit on the line: the unit the quantity was entered in (null = primary)
    /// and the factor frozen when the line was written. See <c>InvoiceLine</c>.</summary>
    public Guid? UnitId { get; private set; }

    /// <inheritdoc cref="UnitId"/>
    public decimal ConversionFactor { get; private set; }

    /// <summary>The quantity the physical ledger receives.</summary>
    public PrimaryQuantity PrimaryQuantity => PrimaryQuantity.FromEntered(Quantity, ConversionFactor);

    private GoodsReceivedNoteLine()
    {
    }

    internal static GoodsReceivedNoteLine Create(
        Guid goodsReceivedNoteId, Guid productId, decimal quantity, decimal rate, VatRate vatRate,
        decimal discountPct, decimal headerDiscountPct, Guid? unitId, decimal conversionFactor)
    {
        var grossAmount = quantity * rate;
        var netAfterLineDiscount = grossAmount * (1 - discountPct / 100m);
        var amount = netAfterLineDiscount * (1 - headerDiscountPct / 100m);

        return new GoodsReceivedNoteLine
        {
            Id = Guid.NewGuid(),
            GoodsReceivedNoteId = goodsReceivedNoteId,
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
