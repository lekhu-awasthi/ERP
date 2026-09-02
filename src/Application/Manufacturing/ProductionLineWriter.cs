using ErpApp.Domain.Manufacturing;

namespace ErpApp.Application.Manufacturing;

/// <summary>
/// The three aggregates carry the same three child shapes but share no base type -- deliberately,
/// since a BOM is master data and the other two are documents with lifecycles. These overloads are
/// the small price of that, and they keep every Create/Update handler from repeating three foreach
/// loops each.
/// </summary>
internal static class ProductionLineWriter
{
    public static void Fill(
        BillOfMaterials target,
        IReadOnlyList<ProductionRawMaterialLineInput> rawMaterials,
        IReadOnlyList<ProductionByProductLineInput> byProducts,
        IReadOnlyList<ProductionExpenseLineInput> expenses)
    {
        foreach (var line in rawMaterials)
        {
            target.AddRawMaterial(line.ProductId, line.Quantity);
        }

        foreach (var line in byProducts)
        {
            target.AddByProduct(line.ProductId, line.CostAllocationPct, line.Quantity);
        }

        foreach (var line in expenses)
        {
            target.AddExpense(line.CostTermId, line.Amount);
        }
    }

    public static void Fill(
        ProductionOrder target,
        IReadOnlyList<ProductionRawMaterialLineInput> rawMaterials,
        IReadOnlyList<ProductionByProductLineInput> byProducts,
        IReadOnlyList<ProductionExpenseLineInput> expenses)
    {
        foreach (var line in rawMaterials)
        {
            target.AddRawMaterial(line.ProductId, line.Quantity);
        }

        foreach (var line in byProducts)
        {
            target.AddByProduct(line.ProductId, line.CostAllocationPct, line.Quantity);
        }

        foreach (var line in expenses)
        {
            target.AddExpense(line.CostTermId, line.Amount);
        }
    }

    public static void Fill(
        ProductionJournal target,
        IReadOnlyList<ProductionRawMaterialLineInput> rawMaterials,
        IReadOnlyList<ProductionByProductLineInput> byProducts,
        IReadOnlyList<ProductionExpenseLineInput> expenses)
    {
        foreach (var line in rawMaterials)
        {
            target.AddRawMaterial(line.ProductId, line.Quantity);
        }

        foreach (var line in byProducts)
        {
            target.AddByProduct(line.ProductId, line.CostAllocationPct, line.Quantity);
        }

        foreach (var line in expenses)
        {
            target.AddExpense(line.CostTermId, line.Amount);
        }
    }
}
