using ErpApp.Application.Sales.Posting;

namespace ErpApp.Application.UnitTests.Sales;

public class CreditNotePostingRuleTests
{
    [Fact]
    public void No_cogs_amount_produces_no_inventory_lines()
    {
        var arAccountId = Guid.NewGuid();
        var vatAccountId = Guid.NewGuid();
        var salesAccountId = Guid.NewGuid();
        var input = new CreditNotePostingInput(
            arAccountId, vatAccountId, [new InvoicePostingLineInput(salesAccountId, 100m, 13m)]);

        var lines = new CreditNotePostingRule().BuildLines(input);

        Assert.Equal(3, lines.Count);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    [Fact]
    public void Cogs_amount_debits_inventory_and_credits_cogs()
    {
        var arAccountId = Guid.NewGuid();
        var vatAccountId = Guid.NewGuid();
        var salesAccountId = Guid.NewGuid();
        var cogsAccountId = Guid.NewGuid();
        var inventoryAccountId = Guid.NewGuid();
        var input = new CreditNotePostingInput(
            arAccountId, vatAccountId, [new InvoicePostingLineInput(salesAccountId, 100m, 13m)],
            cogsAccountId, inventoryAccountId, CogsAmount: 60m);

        var lines = new CreditNotePostingRule().BuildLines(input);

        Assert.Contains(lines, l => l.AccountId == inventoryAccountId && l.Debit == 60m && l.Credit == 0m);
        Assert.Contains(lines, l => l.AccountId == cogsAccountId && l.Debit == 0m && l.Credit == 60m);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    [Fact]
    public void Zero_cogs_amount_with_accounts_set_still_omits_inventory_lines()
    {
        var arAccountId = Guid.NewGuid();
        var vatAccountId = Guid.NewGuid();
        var salesAccountId = Guid.NewGuid();
        var cogsAccountId = Guid.NewGuid();
        var inventoryAccountId = Guid.NewGuid();
        var input = new CreditNotePostingInput(
            arAccountId, vatAccountId, [new InvoicePostingLineInput(salesAccountId, 100m, 13m)],
            cogsAccountId, inventoryAccountId, CogsAmount: 0m);

        var lines = new CreditNotePostingRule().BuildLines(input);

        Assert.DoesNotContain(lines, l => l.AccountId == inventoryAccountId);
        Assert.DoesNotContain(lines, l => l.AccountId == cogsAccountId);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }
}
