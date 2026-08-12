namespace ErpApp.Application.Purchasing.Posting;

/// <summary>
/// Same shape as PurchaseBillPostingInput, including the TDS fields -- a DebitNote is a full
/// mirror of PurchaseBillPostingRule now, including its TDS leg (see DebitNote's doc comment for
/// why the earlier TDS-free version left Accounts Payable unbalanced after a full reversal). Kept
/// as a distinct type (not a reused generic registration) so DI can register a separate
/// IGlPostingRule&lt;T&gt; implementation for the reversed DebitNote posting.
/// </summary>
public sealed record DebitNotePostingInput(
    Guid AccountsPayableAccountId,
    Guid VatReceivableAccountId,
    Guid TdsPayableAccountId,
    decimal TdsAmount,
    IReadOnlyList<PurchaseBillPostingLineInput> Lines);
