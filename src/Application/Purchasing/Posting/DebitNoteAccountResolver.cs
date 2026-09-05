using ErpApp.Application.Common.Persistence;

namespace ErpApp.Application.Purchasing.Posting;

/// <summary>Wraps PurchaseBillAccountResolver -- the resolution logic (Product.PurchaseAccountId
/// fallback, TenantSettings AP/VAT Receivable/TDS Payable accounts) is identical to PurchaseBill's,
/// only the resulting GL directions differ (DebitNotePostingRule). TdsAmount is the DebitNote's
/// own (already resolved from its own lines, same PurchasingValidation.ResolveTdsAmountAsync path
/// PurchaseBill uses) -- not copied from the source bill, so a partial-quantity debit note
/// correctly reverses only its own proportional share.</summary>
internal static class DebitNoteAccountResolver
{
    public static async Task<DebitNotePostingInput> ResolveAsync(
        IAppDbContext db,
        Guid organizationId,
        IEnumerable<(Guid ProductId, decimal Amount, decimal VatAmount)> lines,
        decimal tdsAmount,
        CancellationToken cancellationToken,
        bool requiresLandedCostClearing = false)
    {
        var purchaseBillInput = await PurchaseBillAccountResolver.ResolveAsync(
            db, organizationId, lines, tdsAmount, cancellationToken, requiresLandedCostClearing);

        return new DebitNotePostingInput(
            purchaseBillInput.AccountsPayableAccountId,
            purchaseBillInput.VatReceivableAccountId,
            purchaseBillInput.TdsPayableAccountId,
            purchaseBillInput.TdsAmount,
            purchaseBillInput.Lines,
            purchaseBillInput.InventoryAccountId,
            purchaseBillInput.LandedCostClearingAccountId);
    }
}
