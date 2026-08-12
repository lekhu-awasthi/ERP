namespace ErpApp.Application.Purchasing.Posting;

/// <summary>Same shape as PurchaseBillPostingInput minus the TDS fields -- a DebitNote reversal
/// doesn't reverse the TDS withholding (see phase-6-status.md's scope decisions). Kept as a
/// distinct type (not a reused generic registration) so DI can register a separate
/// IGlPostingRule&lt;T&gt; implementation for the reversed DebitNote posting.</summary>
public sealed record DebitNotePostingInput(
    Guid AccountsPayableAccountId, Guid VatReceivableAccountId, IReadOnlyList<PurchaseBillPostingLineInput> Lines);
