using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Payments.Posting;

/// <summary>Debit the Payment's cash/bank Account, Credit Accounts Receivable, both for Amount --
/// a Customer Payment always fully allocates (Payment.Approve's invariant), so there's no
/// "unallocated remainder" leg to model this phase.</summary>
public sealed class PaymentPostingRule : IGlPostingRule<PaymentPostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(PaymentPostingInput document) =>
    [
        new GlLineInput(document.CashOrBankAccountId, document.Amount, 0),
        new GlLineInput(document.AccountsReceivableAccountId, 0, document.Amount),
    ];
}
