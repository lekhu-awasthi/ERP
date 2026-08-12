using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Purchasing;

/// <summary>Mirror of Sales.CreditNoteLine -- same shape as PurchaseBillLine minus
/// ExpenditureClassification (a reversal doesn't need its own Annex 13 classification).</summary>
public sealed class DebitNoteLine
{
    public Guid Id { get; private set; }
    public Guid DebitNoteId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public VatRate VatRate { get; private set; }
    public decimal Amount { get; private set; }
    public decimal VatAmount { get; private set; }

    private DebitNoteLine()
    {
    }

    internal static DebitNoteLine Create(Guid debitNoteId, Guid productId, decimal quantity, decimal rate, VatRate vatRate)
    {
        var amount = quantity * rate;

        return new DebitNoteLine
        {
            Id = Guid.NewGuid(),
            DebitNoteId = debitNoteId,
            ProductId = productId,
            Quantity = quantity,
            Rate = rate,
            VatRate = vatRate,
            Amount = amount,
            VatAmount = amount * vatRate.ToPercent(),
        };
    }
}
