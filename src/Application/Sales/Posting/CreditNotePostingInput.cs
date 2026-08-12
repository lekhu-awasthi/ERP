namespace ErpApp.Application.Sales.Posting;

/// <summary>Same shape as InvoicePostingInput -- kept as a distinct type (not a reused generic
/// registration) so DI can register a separate IGlPostingRule&lt;T&gt; implementation for the
/// reversed CreditNote posting.</summary>
public sealed record CreditNotePostingInput(
    Guid AccountsReceivableAccountId, Guid VatPayableAccountId, IReadOnlyList<InvoicePostingLineInput> Lines);
