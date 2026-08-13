using ErpApp.Application.Inventory.Posting;

namespace ErpApp.Application.UnitTests.Inventory;

public class InventoryAdjustmentPostingRuleTests
{
    [Fact]
    public void Increase_only_debits_inventory_and_credits_adjustment()
    {
        var inventoryAccountId = Guid.NewGuid();
        var adjustmentAccountId = Guid.NewGuid();
        var input = new InventoryAdjustmentPostingInput(inventoryAccountId, adjustmentAccountId, IncreaseAmount: 500m, DecreaseAmount: 0m);

        var lines = new InventoryAdjustmentPostingRule().BuildLines(input);

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.AccountId == inventoryAccountId && l.Debit == 500m && l.Credit == 0m);
        Assert.Contains(lines, l => l.AccountId == adjustmentAccountId && l.Debit == 0m && l.Credit == 500m);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    [Fact]
    public void Decrease_only_debits_adjustment_and_credits_inventory()
    {
        var inventoryAccountId = Guid.NewGuid();
        var adjustmentAccountId = Guid.NewGuid();
        var input = new InventoryAdjustmentPostingInput(inventoryAccountId, adjustmentAccountId, IncreaseAmount: 0m, DecreaseAmount: 300m);

        var lines = new InventoryAdjustmentPostingRule().BuildLines(input);

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.AccountId == adjustmentAccountId && l.Debit == 300m && l.Credit == 0m);
        Assert.Contains(lines, l => l.AccountId == inventoryAccountId && l.Debit == 0m && l.Credit == 300m);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    [Fact]
    public void Mixed_increase_and_decrease_nets_correctly_on_both_accounts_and_stays_balanced()
    {
        var inventoryAccountId = Guid.NewGuid();
        var adjustmentAccountId = Guid.NewGuid();
        // Increase 500, Decrease 300 -- net effect on Inventory should be +200 (Debit 500 - Credit 300).
        var input = new InventoryAdjustmentPostingInput(inventoryAccountId, adjustmentAccountId, IncreaseAmount: 500m, DecreaseAmount: 300m);

        var lines = new InventoryAdjustmentPostingRule().BuildLines(input);

        Assert.Equal(4, lines.Count);
        var inventoryLines = lines.Where(l => l.AccountId == inventoryAccountId).ToList();
        var adjustmentLines = lines.Where(l => l.AccountId == adjustmentAccountId).ToList();

        Assert.Equal(200m, inventoryLines.Sum(l => l.Debit) - inventoryLines.Sum(l => l.Credit));
        Assert.Equal(-200m, adjustmentLines.Sum(l => l.Debit) - adjustmentLines.Sum(l => l.Credit));
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    [Fact]
    public void Zero_amounts_produce_no_lines()
    {
        var input = new InventoryAdjustmentPostingInput(Guid.NewGuid(), Guid.NewGuid(), IncreaseAmount: 0m, DecreaseAmount: 0m);

        var lines = new InventoryAdjustmentPostingRule().BuildLines(input);

        Assert.Empty(lines);
    }
}
