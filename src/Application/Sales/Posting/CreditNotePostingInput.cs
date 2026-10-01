namespace ErpApp.Application.Sales.Posting;

/// <summary>Same shape as InvoicePostingInput -- kept as a distinct type (not a reused generic
/// registration) so DI can register a separate IGlPostingRule&lt;T&gt; implementation for the
/// reversed CreditNote posting.
///
/// CogsAccountId/InventoryAccountId/CogsAmount (post-Phase-7 fix) mirror InvoicePostingInput's own
/// fields, but with the GL direction reversed: a CreditNote against a Goods-line Invoice puts stock
/// back (ApproveCreditNoteCommandHandler calls IStockLedgerService.IncrementAsync per line, at the
/// exact weighted-average cost recorded on the matching InvoiceLine.CogsUnitCost), so the GL leg
/// must reverse too -- Debit Inventory / Credit COGS, the exact opposite of InvoicePostingRule's
/// Debit COGS / Credit Inventory. Zero/null for a standalone CreditNote (no ReferrerId) or one
/// whose source Invoice had only Service lines -- see CreditNoteAccountResolver's doc comment.</summary>
public sealed record CreditNotePostingInput(
    Guid AccountsReceivableAccountId,
    Guid VatPayableAccountId,
    IReadOnlyList<InvoicePostingLineInput> Lines,
    Guid? CogsAccountId = null,
    Guid? InventoryAccountId = null,
    decimal CogsAmount = 0)
{
    /// <summary>Phase 63 -- a till refund's service charge given back is debited here (the account the
    /// sale credited it to). Null when the note gives back no service charge.</summary>
    public Guid? ServiceChargeAccountId { get; init; }

    public Guid? RoundingAccountId { get; init; }

    /// <summary>Signed like <c>InvoicePostingInput.RoundOff</c>: positive gives back more than the lines
    /// (debited to rounding), negative less (credited).</summary>
    public decimal RoundOff { get; init; }
}
