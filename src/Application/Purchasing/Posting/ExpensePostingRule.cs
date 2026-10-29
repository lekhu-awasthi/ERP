using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Purchasing.Posting;

/// <summary>Debit each line's own Account directly for its pre-VAT Amount (grouped); Debit VAT
/// Receivable for summed VAT; Credit TDS Payable for TdsAmount if applicable; Credit Accounts
/// Payable for the grand total minus TdsAmount -- same TDS-reduces-AP-credit choice
/// PurchaseBillPostingRule made, kept consistent between the two per phase-6-status.md's scope
/// decisions.
///
/// <para>Phase 99 -- a line's disallowed VAT is debited to the line's own account with its Amount, and VAT Receivable
/// takes only the rest, as on a purchase bill.</para></summary>
public sealed class ExpensePostingRule : IGlPostingRule<ExpensePostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(ExpensePostingInput document)
    {
        var grandTotal = document.Lines.Sum(x => x.Amount + x.VatAmount);

        var lines = new List<GlLineInput>();

        lines.AddRange(document.Lines
            .GroupBy(x => x.AccountId)
            .Select(g => new GlLineInput(g.Key, g.Sum(x => x.Amount + x.DisallowedVat), 0)));

        var claimableVat = document.Lines.Sum(x => x.VatAmount - x.DisallowedVat);
        if (claimableVat > 0)
        {
            lines.Add(new GlLineInput(document.VatReceivableAccountId, claimableVat, 0));
        }

        if (document.TdsAmount > 0)
        {
            lines.Add(new GlLineInput(document.TdsPayableAccountId, 0, document.TdsAmount));
        }

        lines.Add(new GlLineInput(document.AccountsPayableAccountId, 0, grandTotal - document.TdsAmount));

        return lines;
    }
}
