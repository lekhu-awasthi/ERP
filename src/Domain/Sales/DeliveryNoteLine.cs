using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.Sales;

/// <summary>Phase 58 -- child line of <see cref="DeliveryNote"/>, the <see cref="SalesOrderLine"/>
/// shape exactly. Its money is what the document says and is posted nowhere; only
/// <see cref="PrimaryQuantity"/> reaches a ledger.</summary>
public sealed class DeliveryNoteLine
{
    public Guid Id { get; private set; }
    public Guid DeliveryNoteId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public VatRate VatRate { get; private set; }
    public decimal DiscountPct { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }

    /// <summary>Phase 52's unit on the line -- see <c>InvoiceLine</c>.</summary>
    public Guid? UnitId { get; private set; }

    /// <inheritdoc cref="UnitId"/>
    public decimal ConversionFactor { get; private set; }

    /// <summary>The quantity the physical ledger issues.</summary>
    public PrimaryQuantity PrimaryQuantity => PrimaryQuantity.FromEntered(Quantity, ConversionFactor);

    private DeliveryNoteLine()
    {
    }

    internal static DeliveryNoteLine Create(
        Guid deliveryNoteId, Guid productId, decimal quantity, decimal rate, VatRate vatRate,
        decimal discountPct, decimal headerDiscountPct, Guid? unitId, decimal conversionFactor)
    {
        var grossAmount = quantity * rate;
        var netAfterLineDiscount = grossAmount * (1 - discountPct / 100m);
        var amount = netAfterLineDiscount * (1 - headerDiscountPct / 100m);

        return new DeliveryNoteLine
        {
            Id = Guid.NewGuid(),
            DeliveryNoteId = deliveryNoteId,
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
