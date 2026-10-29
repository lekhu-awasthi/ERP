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

    /// <summary>Phase 99 -- what this expense was for (Rule 40); Taxable unless the user says otherwise. See
    /// <see cref="PurchaseBillLine.CreditUse"/>.</summary>
    public VatCreditUse CreditUse { get; private set; }

    /// <summary>Phase 99 -- Rule 41's limit (entertainment, vehicle petrol and the rest), chosen on the line: an expense
    /// names an account, never a product, so there is no product default to prefill it from. Full by default.</summary>
    public VatCreditCategory CreditCategory { get; private set; }

    /// <inheritdoc cref="PurchaseBillLine.DisallowedVat"/>
    public decimal DisallowedVat => VatCredit.DisallowedAtPosting(VatAmount, CreditUse, CreditCategory);

    private ExpenseLine()
    {
    }

    internal static ExpenseLine Create(
        Guid expenseId, Guid accountId, decimal amount, VatRate vatRate, VatCreditUse creditUse, VatCreditCategory creditCategory)
    {
        return new ExpenseLine
        {
            Id = Guid.NewGuid(),
            ExpenseId = expenseId,
            AccountId = accountId,
            Amount = amount,
            VatRate = vatRate,
            VatAmount = amount * vatRate.ToPercent(),
            CreditUse = creditUse,
            CreditCategory = creditCategory,
        };
    }
}
