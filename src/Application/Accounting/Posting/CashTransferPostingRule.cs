using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Accounting.Posting;

/// <summary>
/// Translates CashTransfer's FromAccountId + N (ToAccountId, Amount) fan-out shape into one
/// balanced multi-line GL entry: a single Credit line on FromAccountId for the total amount, one
/// Debit line per destination -- still balanced by construction, same GlJournalEntry.Post path
/// JournalVoucher uses, not a parallel posting path.
/// </summary>
public sealed class CashTransferPostingRule : IGlPostingRule<CashTransfer>
{
    public IReadOnlyList<GlLineInput> BuildLines(CashTransfer document)
    {
        var lines = new List<GlLineInput>
        {
            new(document.FromAccountId, 0, document.Lines.Sum(x => x.Amount)),
        };

        lines.AddRange(document.Lines.Select(x => new GlLineInput(x.ToAccountId, x.Amount, 0)));

        return lines;
    }
}
