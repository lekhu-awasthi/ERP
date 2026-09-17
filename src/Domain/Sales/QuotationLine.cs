using ErpApp.Domain.Common;
using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Sales;

/// <summary>
/// Child line of Quotation -- own table, created only via Quotation.AddLine. VatRate is a
/// snapshot of the Product's VatRate at add-time (not a live FK lookup), same "don't let a later
/// master-data edit silently change an already-quoted line" reasoning the roadmap brief calls
/// out. Amount/VatAmount are computed once at AddLine time and persisted (not EF computed
/// columns) -- Quantity*Rate and Amount*VatRate.ToPercent() respectively.
/// </summary>
public sealed class QuotationLine
{
    public Guid Id { get; private set; }
    public Guid QuotationId { get; private set; }
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

    private QuotationLine()
    {
    }

    /// <summary>See InvoiceLine.Create's doc comment -- Amount/VatAmount fold in both line and
    /// header DiscountPct.</summary>
    internal static QuotationLine Create(
        Guid quotationId, Guid productId, decimal quantity, decimal rate, VatRate vatRate,
        decimal discountPct, decimal headerDiscountPct, Guid? unitId, decimal conversionFactor)
    {
        var grossAmount = quantity * rate;
        var netAfterLineDiscount = grossAmount * (1 - discountPct / 100m);
        var amount = netAfterLineDiscount * (1 - headerDiscountPct / 100m);

        return new QuotationLine
        {
            Id = Guid.NewGuid(),
            QuotationId = quotationId,
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
