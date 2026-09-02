namespace ErpApp.Domain.Manufacturing;

/// <summary>
/// The handful of line-level invariants shared by all three manufacturing aggregates. Kept in one
/// place because BillOfMaterials, ProductionOrder and ProductionJournal carry the *same* three
/// child shapes (raw material / by-product / expense) and must reject the same nonsense
/// identically -- a BOM that allows a 150% by-product allocation while the Journal built from it
/// refuses one is a difference nobody could explain.
/// </summary>
internal static class ProductionLineRules
{
    public static void EnsurePositiveQuantity(decimal quantity, string subject)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException($"{subject} needs a positive Quantity.");
        }
    }

    public static void EnsureNonNegativeAmount(decimal amount, string subject)
    {
        if (amount < 0)
        {
            throw new InvalidOperationException($"{subject}'s Amount cannot be negative.");
        }
    }

    public static void EnsureAllocationPercentageInRange(decimal costAllocationPct)
    {
        if (costAllocationPct < 0 || costAllocationPct >= 100)
        {
            throw new InvalidOperationException(
                "A by-product's % of Cost must be at least 0 and less than 100.");
        }
    }

    public static void EnsureAllocationTotalUnderOneHundred(decimal total)
    {
        if (total >= 100)
        {
            throw new InvalidOperationException(
                $"By-products are allocated {total}% of the cost of production in total, which leaves the " +
                "finished goods nothing. The total must be less than 100%.");
        }
    }
}
