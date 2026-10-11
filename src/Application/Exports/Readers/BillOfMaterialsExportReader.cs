using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Exports;
using ErpApp.Domain.Manufacturing;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>
/// Phase 80 -- every Bill of Materials, one row per part: the <i>Output</i> (its finished product and output quantity),
/// then its <i>Raw Material</i>, <i>By-Product</i> and <i>Expense</i> rows, labelled as on the Production sheet so the two
/// read alike.
///
/// <para><b>Master data, so not date-filtered</b>, and placed after the Chart of Accounts in the workbook's order: a
/// recipe is bounded by the catalogue, not by activity, and nothing else in the export says what a product is made of.
/// A product has at most one BOM (a unique index), so the finished product's code identifies it on every row. A BOM
/// with no lines still has its Output row.</para>
///
/// <para>Four queries merged in memory, phase 77's shape.</para>
/// </summary>
public sealed class BillOfMaterialsExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.BillsOfMaterials;

    public string SheetName => "Bills of Materials";

    public bool IsDateFiltered => false;

    /// <summary>Every figure a BOM stores that this sheet carries; <c>ManufacturingExportTests</c> holds the four types
    /// to it.</summary>
    public static readonly IReadOnlySet<(Type Type, string Property)> ExportedFigures = new HashSet<(Type, string)>
    {
        (typeof(BillOfMaterials), nameof(BillOfMaterials.OutputQuantity)),
        (typeof(BomRawMaterialLine), nameof(BomRawMaterialLine.Quantity)),
        (typeof(BomByProductLine), nameof(BomByProductLine.Quantity)),
        (typeof(BomByProductLine), nameof(BomByProductLine.CostAllocationPct)),
        (typeof(BomExpenseLine), nameof(BomExpenseLine.Amount)),
    };

    public IReadOnlyList<string> Headers { get; } =
    [
        "Finished Product Code",
        "Finished Product Name",
        "Active",
        "Manufacture On Every Sale",
        "Row",
        "Product Code",
        "Product Name",
        "Cost Term",
        "Quantity",
        "Unit",
        "Cost Allocation %",
        "Amount",
        "Notes",
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, ExportDateRange range, CancellationToken cancellationToken)
    {
        // Ordered by the finished product's code in SQL as well as in memory, so a capped sheet keeps the same rows the
        // merged order would print first.
        var boms =
            from bom in db.BillsOfMaterials
            where bom.OrganizationId == organizationId
            join finished in db.Products on bom.ProductId equals finished.Id
            select new { bom, FinishedCode = finished.Code };

        var outputs =
            from x in boms
            orderby x.FinishedCode
            select new BomRow(x.bom.ProductId, x.bom.IsActive, x.bom.ManufactureOnEverySale, x.bom.Notes, PartKind.Output,
                x.bom.ProductId, null, x.bom.OutputQuantity, null, null);

        var rawMaterials =
            from line in db.BomRawMaterialLines
            join x in boms on line.BillOfMaterialsId equals x.bom.Id
            orderby x.FinishedCode
            select new BomRow(x.bom.ProductId, x.bom.IsActive, x.bom.ManufactureOnEverySale, x.bom.Notes, PartKind.RawMaterial,
                line.ProductId, null, line.Quantity, null, null);

        var byProducts =
            from line in db.BomByProductLines
            join x in boms on line.BillOfMaterialsId equals x.bom.Id
            orderby x.FinishedCode
            select new BomRow(x.bom.ProductId, x.bom.IsActive, x.bom.ManufactureOnEverySale, x.bom.Notes, PartKind.ByProduct,
                line.ProductId, null, line.Quantity, line.CostAllocationPct, null);

        var expenses =
            from line in db.BomExpenseLines
            join x in boms on line.BillOfMaterialsId equals x.bom.Id
            orderby x.FinishedCode
            select new BomRow(x.bom.ProductId, x.bom.IsActive, x.bom.ManufactureOnEverySale, x.bom.Notes, PartKind.Expense,
                null, line.CostTermId, null, null, line.Amount);

        var totalRowCount = 0;
        var merged = new List<BomRow>();

        foreach (var query in new[] { outputs, rawMaterials, byProducts, expenses })
        {
            totalRowCount += await query.CountAsync(cancellationToken);
            merged.AddRange(await query.Take(maxRows).ToListAsync(cancellationToken));
        }

        var names = await ManufacturingNames.LoadAsync(db, organizationId, cancellationToken);

        var rows = merged
            .OrderBy(r => names.Product(r.FinishedProductId)?.Code, StringComparer.Ordinal)
            .ThenBy(r => r.FinishedProductId)
            .ThenBy(r => r.Kind)
            .Take(maxRows)
            .Select(r => r.ToCells(names))
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }

    private sealed record BomRow(
        Guid FinishedProductId,
        bool IsActive,
        bool ManufactureOnEverySale,
        string? Notes,
        PartKind Kind,
        Guid? ProductId,
        Guid? CostTermId,
        decimal? Quantity,
        decimal? CostAllocationPct,
        decimal? Amount)
    {
        public object?[] ToCells(ManufacturingNames names)
        {
            var finished = names.Product(FinishedProductId);
            var product = names.Product(ProductId);
            return
            [
                finished?.Code,
                finished?.Name,
                IsActive ? "Yes" : "No",
                ManufactureOnEverySale ? "Yes" : "No",
                Kind.Label(),
                product?.Code,
                product?.Name,
                names.CostTerms.NameOf(CostTermId),
                Quantity,
                product is null ? null : names.Units.NameOf(product.PrimaryUnitId),
                CostAllocationPct,
                Amount,
                Notes,
            ];
        }
    }
}
