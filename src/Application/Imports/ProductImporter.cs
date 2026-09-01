using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.UpdateProduct;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Imports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports;

/// <summary>
/// Product bulk import (FR-2.9). Column names mirror the reference product's own
/// <c>new_product_template.xlsx</c>, read live during Phase 21a's confirm-live pass, wherever this
/// codebase has the corresponding field.
///
/// <para><b>Four of the reference template's columns are deliberately absent, and the reason is the
/// same each time -- this codebase has nowhere to put them:</b>
/// <list type="bullet">
/// <item><c>Sales Account</c>/<c>Sales Return Account</c>/<c>Purchase Account</c>/<c>Purchase Return
/// Account</c> -- <c>Product</c> has these fields but <c>CreateProductCommand</c> does not take
/// them (only <c>UpdateProductCommand</c> does, via <c>SetAccounts</c>), so a create-mode import
/// could not set them and an update-mode-only column would be a trap. Update mode preserves
/// whatever is already there rather than blanking it.</item>
/// <item><c>Valuation Method</c> -- <c>Product.Create</c> does not accept one.</item>
/// <item><c>Opening Quantity</c>/<c>Opening Rate</c> -- opening stock is <c>OpeningStockLine</c>, a
/// separate "day zero" transaction with its own screen and its own GL consequences, not a product
/// attribute. Folding it in here would have this importer quietly writing inventory.</item>
/// <item><c>SKU</c> -- no such field exists on <c>Product</c>.</item>
/// </list>
/// Adding a column later is additive and harmless; shipping one that silently does nothing is not.</para>
/// </summary>
public sealed class ProductImporter(IAppDbContext db, ISender sender) : IEntityImporter
{
    private const string ColumnCode = "Product Code";
    private const string ColumnHsCode = "HS Code";
    private const string ColumnType = "Product Type";
    private const string ColumnName = "Product Name";
    private const string ColumnCategory = "Category";
    private const string ColumnVat = "VAT Applicable";
    private const string ColumnPrimaryUnit = "Primary Unit";
    private const string ColumnSellingPrice = "Selling Price";
    private const string ColumnPurchasePrice = "Purchase Price";
    private const string ColumnReorderLevel = "Reorder Level";
    private const string ColumnTrackInventory = "Track Inventory";
    private const string ColumnAvailableForSale = "Available For Sale";

    private static readonly Dictionary<string, ProductType> ProductTypes =
        new(StringComparer.OrdinalIgnoreCase) { ["Goods"] = ProductType.Goods, ["Service"] = ProductType.Service };

    /// <summary>The reference template's instruction reads: VAT Applicable is "Yes" if 13% VAT and
    /// "No" if VAT not applicable. This codebase's <c>VatRate</c> has a third member, ZeroVat
    /// (zero-rated, which is not the same as exempt), so it is offered as an explicit third spelling
    /// rather than being unreachable from an import.</summary>
    private static readonly Dictionary<string, VatRate> VatRates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Yes"] = VatRate.ThirteenPercentVat,
        ["13"] = VatRate.ThirteenPercentVat,
        ["13%"] = VatRate.ThirteenPercentVat,
        ["No"] = VatRate.NoVat,
        ["Zero"] = VatRate.ZeroVat,
        ["0"] = VatRate.ZeroVat,
    };

    public ImportEntityType EntityType => ImportEntityType.Product;

    public ImportTemplateDefinition Template { get; } = new(
        ImportEntityType.Product,
        SheetName: "Products",
        FileNameStem: "ProductImportTemplate",
        Columns:
        [
            new ImportColumn(ColumnCode, Required: false),
            new ImportColumn(ColumnHsCode, Required: false),
            new ImportColumn(ColumnType, Required: true),
            new ImportColumn(ColumnName, Required: true),
            new ImportColumn(ColumnCategory, Required: true),
            new ImportColumn(ColumnVat, Required: true),
            new ImportColumn(ColumnPrimaryUnit, Required: true),
            new ImportColumn(ColumnSellingPrice, Required: false),
            new ImportColumn(ColumnPurchasePrice, Required: false),
            new ImportColumn(ColumnReorderLevel, Required: false),
            new ImportColumn(ColumnTrackInventory, Required: false),
            new ImportColumn(ColumnAvailableForSale, Required: false),
        ],
        SampleRow: ["", "1905", "Goods", "Extra Energy Biscuit", "Snacks", "Yes", "Box", "150", "120", "10", "Yes", "Yes"],
        Instructions:
        [
            "Instruction",
            "- ** marks a required field.",
            "- Leave Product Code blank when creating: it is generated automatically.",
            "- In Update Existing Records mode, Product Code is required and must match an existing product.",
            "- \"Category\" must exactly match a Product Category name already in this organization.",
            "- \"Primary Unit\" must exactly match a Unit of Measurement name already in this organization.",
            "- Product Type: \"Goods\" or \"Service\".",
            "- VAT Applicable: \"Yes\" for 13% VAT, \"No\" for not applicable, \"Zero\" for zero-rated.",
            "- Track Inventory / Available For Sale: Yes or No.",
            "Note: Do not change the column headers.",
        ]);

    public async Task<ImportRowResult> ApplyAsync(
        Guid organizationId, ImportMode mode, ImportRowReader row, CancellationToken cancellationToken)
    {
        var name = row.GetRequiredString(ColumnName);
        var categoryId = await ResolveCategoryAsync(organizationId, row, cancellationToken);
        var primaryUnitId = await ResolvePrimaryUnitAsync(organizationId, row, cancellationToken);
        var vatRate = row.GetChoice(ColumnVat, VatRates, required: true, VatRate.NoVat);
        var hsCode = row.GetOptionalString(ColumnHsCode);
        var sellingPrice = row.GetOptionalDecimal(ColumnSellingPrice);
        var purchasePrice = row.GetOptionalDecimal(ColumnPurchasePrice);
        var reorderLevel = row.GetOptionalInt(ColumnReorderLevel);

        if (mode == ImportMode.CreateNew)
        {
            var type = row.GetChoice(ColumnType, ProductTypes, required: true, ProductType.Goods);

            var created = await sender.Send(
                new CreateProductCommand(
                    organizationId,
                    type,
                    name,
                    categoryId,
                    primaryUnitId,
                    hsCode,
                    AvailableForSale: row.GetOptionalBoolean(ColumnAvailableForSale, fallback: true),
                    sellingPrice,
                    purchasePrice,
                    vatRate,
                    reorderLevel,
                    TrackInventory: row.GetOptionalBoolean(ColumnTrackInventory, fallback: type == ProductType.Goods)),
                cancellationToken);

            return new ImportRowResult(created.Id, created.Code);
        }

        var existing = await FindByCodeAsync(organizationId, row, cancellationToken);

        // Product Type is immutable by design (see ProductType's doc comment), so update mode treats
        // a mismatch as a row error rather than ignoring the column -- silently importing a "Service"
        // row against a Goods product would leave the file and the database disagreeing.
        var declaredType = row.GetChoice(ColumnType, ProductTypes, required: true, ProductType.Goods);
        if (declaredType != existing.Type)
        {
            throw new ImportRowException(
                ColumnType,
                $"Product '{existing.Code}' is a {existing.Type} product; product type cannot be changed by import.");
        }

        var updated = await sender.Send(
            new UpdateProductCommand(
                organizationId,
                existing.Id,
                name,
                categoryId,
                primaryUnitId,
                hsCode,
                AvailableForSale: row.GetOptionalBoolean(ColumnAvailableForSale, fallback: existing.AvailableForSale),
                sellingPrice,
                purchasePrice,
                vatRate,
                reorderLevel,
                TrackInventory: row.GetOptionalBoolean(ColumnTrackInventory, fallback: existing.TrackInventory),
                // Import never deactivates and never clears the default GL accounts: both are set
                // elsewhere and neither has a template column, so they are read back and passed
                // through. UpdateProductCommandHandler calls SetAccounts unconditionally, so
                // omitting them would blank them.
                IsActive: existing.IsActive,
                existing.SalesAccountId,
                existing.SalesReturnAccountId,
                existing.PurchaseAccountId,
                existing.PurchaseReturnAccountId),
            cancellationToken);

        return new ImportRowResult(updated.Id, existing.Code);
    }

    private async Task<Guid> ResolveCategoryAsync(
        Guid organizationId, ImportRowReader row, CancellationToken cancellationToken)
    {
        var categoryName = row.GetRequiredString(ColumnCategory);

        // Every lookup here is filtered by OrganizationId, which is what makes an import for one
        // tenant unable to resolve, match against, or update another tenant's data.
        var categoryId = await db.ProductCategories
            .Where(x => x.OrganizationId == organizationId && x.Name == categoryName)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return categoryId ?? throw new ImportRowException(
            ColumnCategory, $"Product category '{categoryName}' does not exist in this organization.");
    }

    private async Task<Guid> ResolvePrimaryUnitAsync(
        Guid organizationId, ImportRowReader row, CancellationToken cancellationToken)
    {
        var unitName = row.GetRequiredString(ColumnPrimaryUnit);

        var unitId = await db.UnitsOfMeasurement
            .Where(x => x.OrganizationId == organizationId && x.Name == unitName)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return unitId ?? throw new ImportRowException(
            ColumnPrimaryUnit, $"Unit of measurement '{unitName}' does not exist in this organization.");
    }

    private async Task<Product> FindByCodeAsync(
        Guid organizationId, ImportRowReader row, CancellationToken cancellationToken)
    {
        var code = row.GetOptionalString(ColumnCode)
            ?? throw new ImportRowException(
                ColumnCode, $"'{ColumnCode}' is required when updating existing records.");

        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Code == code, cancellationToken);

        return product ?? throw new ImportRowException(
            ColumnCode, $"No product with code '{code}' exists in this organization.");
    }
}
