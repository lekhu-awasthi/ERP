using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Payments;

namespace ErpApp.Application.Payments.Posting;

/// <summary>Direction=Received: Debit the Payment's cash/bank Account, Credit Accounts Receivable.
/// Direction=Paid: exact mirror -- Debit Accounts Payable, Credit the cash/bank Account (confirmed
/// live in erp-module-scan.md's hands-on pass item 11: "Debit [Supplier/AP account] / Credit [cash
/// bank account] -- exact mirror of Customer Payment's posting"). A Payment's allocations sum to
/// at most its Amount (phase-17 decision #1), so there is no "unallocated remainder" leg to model.
///
/// <para><b>Phase 28 -- the realised forex leg.</b> When the payment's rate differs from the rate
/// its allocated documents were booked at, relieving the control account at the payment's rate
/// leaves a residue on it; <see cref="PaymentForexInput"/> carries that already-netted,
/// already-converted amount and the account it belongs to (see PaymentForexCalculator for the
/// arithmetic and its sign reasoning). The correction is a second, independently balanced pair
/// appended to the two lines above -- the same construction Phase 7's COGS leg uses on
/// InvoicePostingRule -- so the entry stays balanced by construction and
/// <c>GlJournalEntry.Post</c>'s invariant is never at risk.</para>
///
/// <para>In both directions a <b>gain debits the control account and credits the forex account</b>,
/// and a loss does the reverse. That reads as a coincidence and is not one: on the receivable side
/// a gain means more base currency arrived than was booked, leaving a credit residue on AR to clear
/// with a debit; on the payable side a gain means less base currency left than was booked, leaving
/// a credit residue on AP to clear with the same debit.</para>
/// </summary>
public sealed class PaymentPostingRule : IGlPostingRule<PaymentPostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(PaymentPostingInput document)
    {
        var lines = document.Direction == PaymentDirection.Received
            ?
            [
                new GlLineInput(document.CashOrBankAccountId, document.Amount, 0),
                new GlLineInput(document.ControlAccountId, 0, document.Amount),
            ]
            : new List<GlLineInput>
            {
                new(document.ControlAccountId, document.Amount, 0),
                new(document.CashOrBankAccountId, 0, document.Amount),
            };

        if (document.Forex is { Amount: > 0 } forex)
        {
            if (forex.IsGain)
            {
                lines.Add(new GlLineInput(document.ControlAccountId, forex.Amount, 0));
                lines.Add(new GlLineInput(forex.ForexAccountId, 0, forex.Amount));
            }
            else
            {
                lines.Add(new GlLineInput(document.ControlAccountId, 0, forex.Amount));
                lines.Add(new GlLineInput(forex.ForexAccountId, forex.Amount, 0));
            }
        }

        return lines;
    }
}
