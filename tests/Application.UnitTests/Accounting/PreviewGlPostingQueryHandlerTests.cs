using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Accounting.Queries.PreviewGlPosting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.UnitTests.Accounting;

/// <summary>
/// Proves PreviewGlPostingQueryHandler and the real Approve path build identical GL lines from
/// the same input, via the same JournalVoucherPostingRule instance type -- architecture-spec.md
/// §3.4's "no duplicated debit/credit math" requirement.
/// </summary>
public class PreviewGlPostingQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_the_same_lines_the_posting_rule_would_build_at_approve()
    {
        var organizationId = Guid.NewGuid();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var lines = new List<JournalVoucherLineInput>
        {
            new(accountA, 250m, 0m),
            new(accountB, 0m, 250m),
        };
        var handler = new PreviewGlPostingQueryHandler(new JournalVoucherPostingRule());

        var preview = await handler.Handle(
            new PreviewGlPostingQuery(organizationId, new DateOnly(2026, 1, 1), "Ref", lines), CancellationToken.None);

        var journalVoucher = JournalVoucher.Create(organizationId, new DateOnly(2026, 1, 1), "Ref");
        foreach (var line in lines)
        {
            journalVoucher.AddLine(line.AccountId, line.Debit, line.Credit);
        }

        var expected = new JournalVoucherPostingRule().BuildLines(journalVoucher);

        Assert.Equal(expected.Count, preview.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].AccountId, preview[i].AccountId);
            Assert.Equal(expected[i].Debit, preview[i].Debit);
            Assert.Equal(expected[i].Credit, preview[i].Credit);
        }
    }
}
