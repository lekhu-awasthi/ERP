using ErpApp.Application.Common.Persistence;

namespace ErpApp.Application.Sales.Posting;

/// <summary>Wraps InvoiceAccountResolver -- the resolution logic (Product.SalesAccountId
/// fallback, TenantSettings AR/VAT accounts) is identical to Invoice's, only the resulting GL
/// directions differ (CreditNotePostingRule).</summary>
internal static class CreditNoteAccountResolver
{
    public static async Task<CreditNotePostingInput> ResolveAsync(
        IAppDbContext db,
        Guid organizationId,
        IEnumerable<(Guid ProductId, decimal Amount, decimal VatAmount)> lines,
        bool resolveInventoryAccounts,
        CancellationToken cancellationToken)
    {
        // resolveInventoryAccounts (post-Phase-7 fix): ApproveCreditNoteCommandHandler passes true
        // only when this CreditNote actually reverses stock against a Goods-line source Invoice --
        // see that handler's doc comment. A standalone CreditNote, or one against an all-Service
        // Invoice, passes false the same way an all-Service Invoice itself does.
        // Phase 61 -- a credit note line carries no service charge (returns at the till are phase 63),
        // so it is resolved as an invoice line whose service charge is zero.
        var invoiceInput = await InvoiceAccountResolver.ResolveAsync(
            db, organizationId, lines.Select(x => (x.ProductId, x.Amount, x.VatAmount, 0m)),
            resolveInventoryAccounts, cancellationToken);

        return new CreditNotePostingInput(
            invoiceInput.AccountsReceivableAccountId, invoiceInput.VatPayableAccountId, invoiceInput.Lines,
            invoiceInput.CogsAccountId, invoiceInput.InventoryAccountId);
    }
}
