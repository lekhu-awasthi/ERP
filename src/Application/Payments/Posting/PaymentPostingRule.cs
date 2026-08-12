using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Payments;

namespace ErpApp.Application.Payments.Posting;

/// <summary>Direction=Received: Debit the Payment's cash/bank Account, Credit Accounts Receivable.
/// Direction=Paid: exact mirror -- Debit Accounts Payable, Credit the cash/bank Account (confirmed
/// live in erp-module-scan.md's hands-on pass item 11: "Debit [Supplier/AP account] / Credit [cash
/// bank account] -- exact mirror of Customer Payment's posting"). Both directions always fully
/// allocate (Payment.Approve's invariant), so there's no "unallocated remainder" leg to model this
/// phase.</summary>
public sealed class PaymentPostingRule : IGlPostingRule<PaymentPostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(PaymentPostingInput document) =>
        document.Direction == PaymentDirection.Received
            ?
            [
                new GlLineInput(document.CashOrBankAccountId, document.Amount, 0),
                new GlLineInput(document.ControlAccountId, 0, document.Amount),
            ]
            :
            [
                new GlLineInput(document.ControlAccountId, document.Amount, 0),
                new GlLineInput(document.CashOrBankAccountId, 0, document.Amount),
            ];
}
