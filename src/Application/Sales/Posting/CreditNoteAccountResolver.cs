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
        CancellationToken cancellationToken)
    {
        var invoiceInput = await InvoiceAccountResolver.ResolveAsync(db, organizationId, lines, cancellationToken);

        return new CreditNotePostingInput(invoiceInput.AccountsReceivableAccountId, invoiceInput.VatPayableAccountId, invoiceInput.Lines);
    }
}
