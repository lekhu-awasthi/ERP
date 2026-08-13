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
///
/// CogsAccountId/InventoryAccountId/CogsAmount (Phase 7) carry the COGS leg's already-resolved
/// accounts and already-computed amount -- ApproveInvoiceCommandHandler resolves the accounts via
/// InvoiceAccountResolver up front (same fail-fast-before-any-side-effect precedent as AR/VAT),
/// then re-computes CogsAmount from IStockLedgerService.ConsumeAsync's actual result and threads
/// it back in with a `with` expression, since the real FIFO cost isn't known until Consume runs.
/// CogsAmount is 0 (and the two account fields null) for an all-Service invoice or for the
/// GL-preview-before-approve query, which deliberately never estimates a COGS leg -- see
/// InvoiceAccountResolver's doc comment and phase-7-status.md's scope decision.
/// </summary>
public sealed record InvoicePostingInput(
    Guid AccountsReceivableAccountId,
    Guid VatPayableAccountId,
    IReadOnlyList<InvoicePostingLineInput> Lines,
    Guid? CogsAccountId = null,
    Guid? InventoryAccountId = null,
    decimal CogsAmount = 0);

public sealed record InvoicePostingLineInput(Guid SalesAccountId, decimal Amount, decimal VatAmount);
