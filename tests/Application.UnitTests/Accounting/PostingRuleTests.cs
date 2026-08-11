using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.UnitTests.Accounting;

public class PostingRuleTests
{
    [Fact]
    public void JournalVoucherPostingRule_maps_lines_one_to_one()
    {
        var journalVoucher = JournalVoucher.Create(Guid.NewGuid(), new DateOnly(2026, 1, 1), null);
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        journalVoucher.AddLine(accountA, 100m, 0m);
        journalVoucher.AddLine(accountB, 0m, 100m);

        var lines = new JournalVoucherPostingRule().BuildLines(journalVoucher);

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.AccountId == accountA && l.Debit == 100m && l.Credit == 0m);
        Assert.Contains(lines, l => l.AccountId == accountB && l.Debit == 0m && l.Credit == 100m);
    }

    [Fact]
    public void CashTransferPostingRule_credits_from_account_and_debits_each_destination()
    {
        var fromAccountId = Guid.NewGuid();
        var toAccountA = Guid.NewGuid();
        var toAccountB = Guid.NewGuid();
        var cashTransfer = CashTransfer.Create(Guid.NewGuid(), new DateOnly(2026, 1, 1), null, fromAccountId);
        cashTransfer.AddLine(toAccountA, 400m);
        cashTransfer.AddLine(toAccountB, 600m);

        var lines = new CashTransferPostingRule().BuildLines(cashTransfer);

        Assert.Equal(3, lines.Count);
        Assert.Contains(lines, l => l.AccountId == fromAccountId && l.Credit == 1000m && l.Debit == 0m);
        Assert.Contains(lines, l => l.AccountId == toAccountA && l.Debit == 400m && l.Credit == 0m);
        Assert.Contains(lines, l => l.AccountId == toAccountB && l.Debit == 600m && l.Credit == 0m);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }
}
