using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Exports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>FR-2.8's "products" category. Foreign keys are resolved to the names a human reads --
/// a sheet full of GUIDs would satisfy the letter of the requirement and none of its point.</summary>
public sealed class ProductExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.Products;

    public string SheetName => "Products";

    public IReadOnlyList<string> Headers { get; } =
    [
        "Product Code",
        "Product Name",
        "Product Type",
        "Category",
        "Primary Unit",
        "HS Code",
        "VAT",
        "Selling Price",
        "Purchase Price",
        "Reorder Level",
        "Valuation Method",
        "Track Inventory",
        "Available For Sale",
        "Active",
        "Created At",
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, CancellationToken cancellationToken)
    {
        var query =
            from product in db.Products
            where product.OrganizationId == organizationId
            join category in db.ProductCategories on product.CategoryId equals category.Id into categories
            from category in categories.DefaultIfEmpty()
            join unit in db.UnitsOfMeasurement on product.PrimaryUnitId equals unit.Id into units
            from unit in units.DefaultIfEmpty()
            orderby product.Code
            select new
            {
                product.Code,
                product.Name,
                product.Type,
                CategoryName = category == null ? null : category.Name,
                UnitName = unit == null ? null : unit.Name,
                product.HsCode,
                product.VatRate,
                product.SellingPrice,
                product.PurchasePrice,
                product.ReOrderLevel,
                product.ValuationMethod,
                product.TrackInventory,
                product.AvailableForSale,
                product.IsActive,
                product.CreatedAt,
            };

        var totalRowCount = await query.CountAsync(cancellationToken);
        var page = await query.Take(maxRows).ToListAsync(cancellationToken);

        // Projected to cells in memory, not in the query: enum-to-string and the Nepal-local stamp
        // are both things EF should not be asked to translate.
        var rows = page
            .Select(p => new object?[]
            {
                p.Code,
                p.Name,
                p.Type.ToString(),
                p.CategoryName,
                p.UnitName,
                p.HsCode,
                p.VatRate.ToString(),
                p.SellingPrice,
                p.PurchasePrice,
                p.ReOrderLevel,
                p.ValuationMethod.ToString(),
                p.TrackInventory,
                p.AvailableForSale,
                p.IsActive,
                ExportCell.LocalTimestamp(p.CreatedAt),
            })
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }
}
