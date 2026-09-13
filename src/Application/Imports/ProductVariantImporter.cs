using ErpApp.Application.Catalog.Commands.CreateProductVariant;
using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports;

/// <summary>
/// Variant bulk import (FR-8.3), the phase-24 carried item.
///
/// <para><b>This upload type is an addition, not parity, and the status doc labels it as one.</b>
/// The reference product's Upload Type list is exactly the seven read live in Phase 21a and a
/// variant importer is not among them, so there is no template to mirror and no instruction text to
/// honour -- every column below is a decision rather than an observation. Phase 24 deferred this as
/// "a template-design task of its own"; the design is here.</para>
///
/// <para><b>Three attribute slots, and the cap is stated rather than discovered.</b> A variant's
/// combination is an open-ended set of (attribute, option) pairs, which a rectangle cannot hold
/// without either one column per attribute in the tenant's catalogue (a template whose shape changes
/// when somebody adds a colour) or a single cell of encoded pairs (unreadable, and unparseable the
/// first time an option value contains the separator). Three fixed Attribute/Value pairs covers
/// what the live product actually carries -- its richest example offers four colours by three sizes,
/// i.e. two attributes -- and a fourth is an additive column if anyone needs one.</para>
///
/// <para><b>The parent must already offer the combination.</b> <c>Product.CreateVariant</c> refuses
/// an option the parent does not list in its "Attributes Used" pool, and this importer does not
/// silently widen that pool: doing so would let a typo in one row add a permanent option to a
/// product's matrix. Set the pool on the product's Variants tab first; a row naming an option
/// outside it is rejected with the option named.</para>
///
/// <para><b>Create-only.</b> A variant's identity is its combination, so "update" would either mean
/// moving it to a different combination -- which is a different variant -- or editing its prices,
/// which <c>ProductImporter</c> already does by product code, because a variant <i>is</i> a
/// Product.</para>
/// </summary>
public sealed class ProductVariantImporter(IAppDbContext db) : IEntityImporter
{
    private const string ColumnParentCode = "Parent Product Code";
    private const string ColumnName = "Variant Name";
    private const string ColumnSku = "SKU";
    private const string ColumnBarcode = "Barcode";
    private const string ColumnSellingPrice = "Selling Price";
    private const string ColumnPurchasePrice = "Purchase Price";

    private static readonly (string Attribute, string Value)[] AttributeSlots =
    [
        ("Attribute 1", "Value 1"),
        ("Attribute 2", "Value 2"),
        ("Attribute 3", "Value 3"),
    ];

    public ImportEntityType EntityType => ImportEntityType.ProductVariant;

    public ImportTemplateDefinition Template { get; } = new(
        ImportEntityType.ProductVariant,
        SheetName: "Variants",
        FileNameStem: "ProductVariantImportTemplate",
        Columns:
        [
            new ImportColumn(ColumnParentCode, Required: true),
            new ImportColumn("Attribute 1", Required: true),
            new ImportColumn("Value 1", Required: true),
            new ImportColumn("Attribute 2", Required: false),
            new ImportColumn("Value 2", Required: false),
            new ImportColumn("Attribute 3", Required: false),
            new ImportColumn("Value 3", Required: false),
            new ImportColumn(ColumnName, Required: false),
            new ImportColumn(ColumnSku, Required: false),
            new ImportColumn(ColumnBarcode, Required: false),
            new ImportColumn(ColumnSellingPrice, Required: false),
            new ImportColumn(ColumnPurchasePrice, Required: false),
        ],
        SampleRow:
        [
            "P0007", "Colour", "Blue", "Size", "L", "", "", "", "TSH-BL-L", "5901234123457", "1200", "900",
        ],
        Instructions:
        [
            "Instruction",
            "- ** marks a required field.",
            "- \"Parent Product Code\" must match a product that already has variants configured:",
            "  open it, set its Attributes Used on the Variants tab, and import afterwards.",
            "- Each Attribute / Value pair must name an attribute and an option the parent product",
            "  already offers. Leave slots 2 and 3 blank when a variant uses fewer attributes.",
            "- An attribute may not appear twice on one row.",
            "- Leave \"Variant Name\" blank to have it composed from the parent name and the option",
            "  values, which is what the product does when you add a variant by hand.",
            "- This upload creates variants only. Edit an existing variant's prices through the",
            "  Product upload, using the variant's own product code.",
            "Note: Do not change the column headers.",
        ]);

    public async Task<ImportRowPlan> PlanAsync(
        ImportRowContext context, ImportRowReader row, CancellationToken cancellationToken)
    {
        if (context.Mode != ImportMode.CreateNew)
        {
            throw new ImportRowException(
                null, "Variants can only be created by import, not updated. Re-upload with Create New Records.");
        }

        var parentCode = row.GetRequiredString(ColumnParentCode);

        var parent = await db.Products
            .AsNoTracking()
            .Where(x => x.OrganizationId == context.OrganizationId && x.Code == parentCode)
            .Select(x => new { x.Id, x.Name, x.HasVariants, x.ParentProductId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ImportRowException(
                ColumnParentCode, $"No product with code '{parentCode}' exists in this organization.");

        if (parent.ParentProductId is not null)
        {
            throw new ImportRowException(
                ColumnParentCode, $"'{parentCode}' is itself a variant; a variant cannot have variants.");
        }

        if (!parent.HasVariants)
        {
            throw new ImportRowException(
                ColumnParentCode,
                $"'{parent.Name}' has no variant attributes configured yet. Open the product's Variants tab, "
                    + "set its Attributes Used, then import.");
        }

        var combination = await ResolveCombinationAsync(context.OrganizationId, parent.Id, row, cancellationToken);

        return ImportRowPlan.For<CreateProductVariantCommand, ProductVariantResult>(
            new CreateProductVariantCommand(
                context.OrganizationId,
                parent.Id,
                combination.Pairs,
                row.GetOptionalString(ColumnName),
                row.GetOptionalString(ColumnSku),
                row.GetOptionalString(ColumnBarcode),
                row.GetOptionalDecimal(ColumnSellingPrice),
                row.GetOptionalDecimal(ColumnPurchasePrice)),
            $"Create variant of '{parent.Name}' ({combination.Describe()})",
            targetCode: null,
            created => new ImportRowResult(created.Id, created.Code));
    }

    /// <summary>
    /// Turns up to three Attribute/Value name pairs into the ids the command wants, refusing
    /// anything the parent does not already offer.
    /// </summary>
    private async Task<ResolvedCombination> ResolveCombinationAsync(
        Guid organizationId, Guid parentId, ImportRowReader row, CancellationToken cancellationToken)
    {
        // The parent's own pool, not the tenant catalogue: an option the tenant has but this product
        // does not offer is exactly the case Product.CreateVariant refuses, and refusing it here
        // names the product instead of throwing a domain message at the user.
        var offered = await db.ProductVariantAttributeUsages
            .Where(u => u.ProductId == parentId)
            .Join(
                db.VariantAttributes.Where(a => a.OrganizationId == organizationId),
                u => u.VariantAttributeId,
                a => a.Id,
                (u, a) => new { u.VariantAttributeId, AttributeName = a.Name, u.VariantAttributeOptionId })
            .Join(
                db.VariantAttributeOptions,
                u => u.VariantAttributeOptionId,
                o => o.Id,
                (u, o) => new OfferedOption(u.VariantAttributeId, u.AttributeName, o.Id, o.Value))
            .ToListAsync(cancellationToken);

        var pairs = new List<VariantCombinationInput>();
        var described = new List<string>();
        var usedAttributes = new HashSet<Guid>();

        foreach (var (attributeColumn, valueColumn) in AttributeSlots)
        {
            var attributeName = row.GetOptionalString(attributeColumn);
            var optionValue = row.GetOptionalString(valueColumn);

            if (attributeName is null && optionValue is null)
            {
                continue;
            }

            if (attributeName is null || optionValue is null)
            {
                throw new ImportRowException(
                    attributeName is null ? attributeColumn : valueColumn,
                    $"'{attributeColumn}' and '{valueColumn}' must be filled in together, or both left blank.");
            }

            var match = offered.Find(
                o => string.Equals(o.AttributeName, attributeName, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(o.OptionValue, optionValue, StringComparison.OrdinalIgnoreCase))
                ?? throw new ImportRowException(
                    valueColumn,
                    $"This product does not offer '{attributeName}: {optionValue}'. Add it to the product's "
                        + "Attributes Used first.");

            if (!usedAttributes.Add(match.AttributeId))
            {
                throw new ImportRowException(
                    attributeColumn, $"'{attributeName}' appears twice on this row; a variant takes one value of each.");
            }

            pairs.Add(new VariantCombinationInput(match.AttributeId, match.OptionId));
            described.Add($"{match.AttributeName}: {match.OptionValue}");
        }

        if (pairs.Count == 0)
        {
            throw new ImportRowException("Attribute 1", "A variant needs at least one attribute value.");
        }

        return new ResolvedCombination(pairs, described);
    }

    private sealed record OfferedOption(Guid AttributeId, string AttributeName, Guid OptionId, string OptionValue);

    private sealed record ResolvedCombination(IReadOnlyList<VariantCombinationInput> Pairs, IReadOnlyList<string> Parts)
    {
        public string Describe() => string.Join(", ", Parts);
    }
}
