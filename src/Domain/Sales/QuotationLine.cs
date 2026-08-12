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
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }

    private QuotationLine()
    {
    }

    internal static QuotationLine Create(Guid quotationId, Guid productId, decimal quantity, decimal rate, VatRate vatRate)
    {
        var amount = quantity * rate;

        return new QuotationLine
        {
            Id = Guid.NewGuid(),
            QuotationId = quotationId,
            ProductId = productId,
            Quantity = quantity,
            Rate = rate,
            VatRate = vatRate,
            Amount = amount,
            VatAmount = amount * vatRate.ToPercent(),
        };
    }
}
