namespace ErpApp.Application.Sales.Posting;

/// <summary>
/// Pure input shape for InvoicePostingRule.BuildLines -- deliberately NOT Domain.Sales.Invoice
/// itself, unlike JournalVoucherPostingRule/CashTransferPostingRule (architecture-spec.md §3.4).
/// Those two rules are pure because a JournalVoucher/CashTransfer's own Lines already *are* its GL
/// lines, 1:1 -- nothing extra to resolve. An Invoice's GL lines need each line's Sales Revenue
/// Account (Product.SalesAccountId, falling back to TenantSettings.DefaultSalesAccountId) plus
/// the tenant's Accounts Receivable/VAT Payable accounts, none of which live on the Invoice
/// aggregate itself -- resolving them requires DB reads IGlPostingRule's "no I/O" contract
/// forbids inside BuildLines. InvoiceAccountResolver (used by both ApproveInvoiceCommandHandler
/// and the Invoice PreviewGlPostingQuery handler, so the resolution logic itself isn't duplicated
/// either) does that resolution once and hands back this plain record, keeping BuildLines a pure
/// function of already-resolved data.
/// </summary>
public sealed record InvoicePostingInput(
    Guid AccountsReceivableAccountId, Guid VatPayableAccountId, IReadOnlyList<InvoicePostingLineInput> Lines);

public sealed record InvoicePostingLineInput(Guid SalesAccountId, decimal Amount, decimal VatAmount);
