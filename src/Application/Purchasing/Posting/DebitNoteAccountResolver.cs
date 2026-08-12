using ErpApp.Application.Common.Persistence;

namespace ErpApp.Application.Purchasing.Posting;

/// <summary>Wraps PurchaseBillAccountResolver -- the resolution logic (Product.PurchaseAccountId
/// fallback, TenantSettings AP/VAT Receivable accounts) is identical to PurchaseBill's, only the
/// resulting GL directions differ (DebitNotePostingRule) and TDS never applies to a
/// reversal.</summary>
internal static class DebitNoteAccountResolver
{
    public static async Task<DebitNotePostingInput> ResolveAsync(
        IAppDbContext db,
        Guid organizationId,
        IEnumerable<(Guid ProductId, decimal Amount, decimal VatAmount)> lines,
        CancellationToken cancellationToken)
    {
        var purchaseBillInput = await PurchaseBillAccountResolver.ResolveAsync(db, organizationId, lines, tdsAmount: 0, cancellationToken);

        return new DebitNotePostingInput(purchaseBillInput.AccountsPayableAccountId, purchaseBillInput.VatReceivableAccountId, purchaseBillInput.Lines);
    }
}
