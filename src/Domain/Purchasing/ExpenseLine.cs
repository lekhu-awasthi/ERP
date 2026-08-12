using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.Purchasing;

/// <summary>
/// Child line of Expense -- confirmed live distinct shape from PurchaseBillLine: no
/// ProductId/Quantity/Rate, each line debits a GL Account directly for a manually-entered Amount
/// (erp-module-scan.md's Purchase Module > Expenses section). VatAmount is still computed from
/// Amount*VatRate.ToPercent(), same as every other line type.
/// </summary>
public sealed class ExpenseLine
{
    public Guid Id { get; private set; }
    public Guid ExpenseId { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Amount { get; private set; }
    public VatRate VatRate { get; private set; }
    public decimal VatAmount { get; private set; }

    private ExpenseLine()
    {
    }

    internal static ExpenseLine Create(Guid expenseId, Guid accountId, decimal amount, VatRate vatRate)
    {
        return new ExpenseLine
        {
            Id = Guid.NewGuid(),
            ExpenseId = expenseId,
            AccountId = accountId,
            Amount = amount,
            VatRate = vatRate,
            VatAmount = amount * vatRate.ToPercent(),
        };
    }
}
