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
///
/// <para><b>Phase 61 -- a till sale's two extra legs.</b> <see cref="ServiceChargeAccountId"/> and
/// <see cref="RoundingAccountId"/> are resolved only when the sale carries a service charge or a
/// round-off (location setting, then tenant default, then a 409 naming the missing one --
/// <c>PosAccountResolver</c>), and are left null for an ERP invoice, whose lines carry no service
/// charge and whose <see cref="RoundOff"/> is zero. They are <b>init properties rather than more
/// trailing constructor parameters</b>: phase 60's lesson is that the Nth trailing optional
/// parameter is how a caller that stopped at N-2 goes on compiling while dropping values.</para>
/// </summary>
public sealed record InvoicePostingInput(
    Guid AccountsReceivableAccountId,
    Guid VatPayableAccountId,
    IReadOnlyList<InvoicePostingLineInput> Lines,
    Guid? CogsAccountId = null,
    Guid? InventoryAccountId = null,
    decimal CogsAmount = 0)
{
    public Guid? ServiceChargeAccountId { get; init; }

    public Guid? RoundingAccountId { get; init; }

    /// <summary>Signed: positive is added to the bill (credited), negative is taken off (debited).</summary>
    public decimal RoundOff { get; init; }
}

/// <summary>One line's posting figures. <see cref="ServiceChargeAmount"/> is zero on every ERP
/// line; on a till line it is credited to the service charge account, and <see cref="VatAmount"/>
/// already includes the VAT on it (it is inside the VAT base).</summary>
public sealed record InvoicePostingLineInput(Guid SalesAccountId, decimal Amount, decimal VatAmount)
{
    public decimal ServiceChargeAmount { get; init; }
}
