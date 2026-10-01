using ErpApp.Application.Sales.Posting;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>Phase 61 -- the settlement entry's arithmetic, without a database.</summary>
public sealed class InvoiceTenderPostingRuleTests
{
    private static readonly Guid Receivable = Guid.NewGuid();
    private static readonly Guid Drawer = Guid.NewGuid();
    private static readonly Guid Bank = Guid.NewGuid();

    [Fact]
    public void Change_is_netted_into_the_drawers_line_and_the_receivable_is_credited_what_was_settled()
    {
        var lines = new InvoiceTenderPostingRule().BuildLines(new InvoiceTenderPostingInput(
            Receivable, [new(Drawer, 500m), new(Bank, 200m)], Drawer, 183m));

        Assert.Equal(317m, lines.Single(x => x.AccountId == Drawer).Debit);
        Assert.Equal(200m, lines.Single(x => x.AccountId == Bank).Debit);
        Assert.Equal(517m, lines.Single(x => x.AccountId == Receivable).Credit);
        Assert.Equal(lines.Sum(x => x.Debit), lines.Sum(x => x.Credit));
    }

    [Fact]
    public void A_cash_tender_netted_to_zero_by_change_leaves_no_line()
    {
        var lines = new InvoiceTenderPostingRule().BuildLines(new InvoiceTenderPostingInput(
            Receivable, [new(Bank, 50m), new(Drawer, 20m)], Drawer, 20m));

        Assert.DoesNotContain(lines, x => x.AccountId == Drawer);
        Assert.Equal(2, lines.Count);
        Assert.Equal(50m, lines.Single(x => x.AccountId == Receivable).Credit);
    }

    [Fact]
    public void Two_tenders_into_one_account_post_one_line()
    {
        var lines = new InvoiceTenderPostingRule().BuildLines(new InvoiceTenderPostingInput(
            Receivable, [new(Bank, 30m), new(Bank, 70m)], null, 0m));

        Assert.Equal(100m, Assert.Single(lines, x => x.AccountId == Bank).Debit);
    }
}
