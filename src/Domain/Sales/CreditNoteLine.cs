using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Sales;

/// <summary>Same shape as InvoiceLine -- see that type's doc comment.</summary>
public sealed class CreditNoteLine
{
    public Guid Id { get; private set; }
    public Guid CreditNoteId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public VatRate VatRate { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }

    private CreditNoteLine()
    {
    }

    internal static CreditNoteLine Create(Guid creditNoteId, Guid productId, decimal quantity, decimal rate, VatRate vatRate)
    {
        var amount = quantity * rate;

        return new CreditNoteLine
        {
            Id = Guid.NewGuid(),
            CreditNoteId = creditNoteId,
            ProductId = productId,
            Quantity = quantity,
            Rate = rate,
            VatRate = vatRate,
            Amount = amount,
            VatAmount = amount * vatRate.ToPercent(),
        };
    }
}
