using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Accounting.Posting;

/// <summary>Trivial rule -- a JournalVoucher's GL posting *is* its own lines, 1:1.</summary>
public sealed class JournalVoucherPostingRule : IGlPostingRule<JournalVoucher>
{
    public IReadOnlyList<GlLineInput> BuildLines(JournalVoucher document) =>
        document.Lines.Select(x => new GlLineInput(x.AccountId, x.Debit, x.Credit)).ToList();
}
