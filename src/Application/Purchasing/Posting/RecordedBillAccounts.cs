using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Posting;

/// <summary>
/// Phase 87 -- the accounts a purchase bill recorded at its approval, read for the debit note that returns it. A line's
/// debit account is found by product: one approval resolves one account per product. A null (a bill from before phase 87,
/// a leg it did not post, a product it never had) leaves the resolver to today's settings.
/// </summary>
internal sealed record RecordedBillAccounts(
    Guid? PayableAccountId,
    Guid? VatReceivableAccountId,
    Guid? TdsPayableAccountId,
    Guid? InventoryAccountId,
    Guid? LandedCostClearingAccountId,
    IReadOnlyDictionary<Guid, Guid> DebitAccountByProduct)
{
    public Guid? DebitAccountFor(Guid productId) =>
        DebitAccountByProduct.TryGetValue(productId, out var accountId) ? accountId : null;

    public static RecordedBillAccounts? Of(PurchaseBill? bill) => bill is null
        ? null
        : new(
            bill.PayableAccountId,
            bill.VatReceivableAccountId,
            bill.TdsPayableAccountId,
            bill.InventoryAccountId,
            bill.LandedCostClearingAccountId,
            bill.Lines
                .Where(x => x.DebitAccountId is not null)
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.First().DebitAccountId!.Value));
}
