using ErpApp.Application.Catalog.Commands.SetProductVariantAttributes;
using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports;

/// <summary>
/// Phase 45 -- bulk setup of a product's "Attributes Used" pool, the phase-38 carried item.
///
/// <para><b>The gap it closes, in phase 38's own words:</b> "Variant import is create-only and needs
/// the parent's attribute pool configured first ... there is no way to set up a product's Attributes
/// Used in bulk." <see cref="ProductVariantImporter"/> refuses a row whose parent does not already
/// offer the combination, and rightly so -- silently widening a pool would let one typo add a
/// permanent option to a product's matrix. But that left the variant importer unusable at the scale
/// it exists for: importing variants for two hundred products needs pools on two hundred products,
/// and the only way to set one was to open each product's Variants tab by hand.</para>
///
/// <para><b>Why an importer rather than a richer control on the form.</b> Phase 38 declined an
/// importer for the landed-cost grid because that grid is generated <i>from the document in front of
/// you</i> and its columns change whenever a tenant adds a cost term. A pool is the opposite: it is
/// a set of <c>(AttributeId, OptionId)</c> pairs, so its file is a fixed three-column rectangle no
/// tenant's data can reshape. That fixed shape is the distinguishing fact, and it is why this is a
/// worn path -- plan/apply, dry run, resumable -- rather than a new mechanism.</para>
///
/// <para><b>One row is one pair, and a row <i>adds</i> to the pool rather than replacing it.</b>
/// <see cref="SetProductVariantAttributesCommand"/> replaces a pool wholesale, which is right for a
/// form that submits the whole set at once and wrong for a file whose rows arrive one at a time: a
/// replace per row would leave each product holding only the last row that named it. So each row
/// re-reads the product's current pool and sends the union. Three consequences, all deliberate:
/// <list type="bullet">
/// <item>Re-running a file is idempotent -- a pair already in the pool produces the same pool.</item>
/// <item>An import can never <i>remove</i> an option, so it can never strand a variant that was
/// built from one. Removal stays on the product's own Variants tab, where the refusal that protects
/// those children already lives.</item>
/// <item>During the dry run nothing is written, so two rows naming one product each plan against
/// the same starting pool. That is correct for a validation pass -- it is checking rows, not
/// accumulating them -- and the apply pass re-plans every row against the database as it goes
/// (<see cref="ImportJobProcessor"/> plans and executes each row in turn), so the union really does
/// accumulate there.</item>
/// </list></para>
///
/// <para><b>Create-only</b>, and the word is about the pool entry rather than the product. A pair is
/// present or absent -- there is no third state to update it to -- and the products themselves must
/// already exist, which is what <see cref="ProductImporter"/> is for. Offering "Update Existing
/// Records" would be a mode with nothing different to do.</para>
///
/// <para><b>No new permission key.</b> It sends <see cref="SetProductVariantAttributesCommand"/>,
/// which rides <c>Catalog.Product.Manage</c> -- choosing which options this product offers is
/// product curation. <c>AuthorizationBehavior</c> re-checks it per row (phase-21a's Decision B).</para>
/// </summary>
public sealed class ProductAttributePoolImporter(IAppDbContext db) : IEntityImporter
{
    private const string ColumnProductCode = "Product Code";
    private const string ColumnAttribute = "Attribute";
    private const string ColumnValue = "Value";

    public ImportEntityType EntityType => ImportEntityType.ProductAttributePool;

    public ImportTemplateDefinition Template { get; } = new(
        ImportEntityType.ProductAttributePool,
        SheetName: "Attributes Used",
        FileNameStem: "ProductAttributePoolImportTemplate",
        Columns:
        [
            new ImportColumn(ColumnProductCode, Required: true),
            new ImportColumn(ColumnAttribute, Required: true),
            new ImportColumn(ColumnValue, Required: true),
        ],
        SampleRow: ["P0007", "Colour", "Blue"],
        Instructions:
        [
            "Instruction",
            "- ** marks a required field.",
            "- One row per option. A product offering 4 colours and 3 sizes takes 7 rows, all",
            "  naming the same \"Product Code\".",
            "- \"Product Code\" must match a product that already exists. Create products with the",
            "  Product upload first.",
            "- \"Attribute\" and \"Value\" must match a variant attribute and one of its options that",
            "  already exist under Products > Variant Attributes.",
            "- Options are added to whatever the product already offers; this upload never removes",
            "  one. Remove an option on the product's own Variants tab, which refuses to drop one a",
            "  variant is built from.",
            "- Giving a product its first option turns it into a variant product: it can no longer be",
            "  put on a document line itself, and its variants are sold and stocked instead.",
            "- Import the variants themselves afterwards, with the Product Variant upload.",
            "Note: Do not change the column headers.",
        ]);

    public async Task<ImportRowPlan> PlanAsync(
        ImportRowContext context, ImportRowReader row, CancellationToken cancellationToken)
    {
        if (context.Mode != ImportMode.CreateNew)
        {
            throw new ImportRowException(
                null,
                "Attributes Used can only be added by import, not updated. Re-upload with Create New Records.");
        }

        var productCode = row.GetRequiredString(ColumnProductCode);
        var attributeName = row.GetRequiredString(ColumnAttribute);
        var optionValue = row.GetRequiredString(ColumnValue);

        var product = await db.Products
            .AsNoTracking()
            .Where(x => x.OrganizationId == context.OrganizationId && x.Code == productCode)
            .Select(x => new { x.Id, x.Name, x.Code, x.ParentProductId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ImportRowException(
                ColumnProductCode, $"No product with code '{productCode}' exists in this organization.");

        // A variant child cannot itself offer options; SetProductVariantAttributesCommand refuses it
        // with a 409, and saying so here makes it a row error the review step can show instead.
        if (product.ParentProductId is not null)
        {
            throw new ImportRowException(
                ColumnProductCode,
                $"'{productCode}' is itself a variant, so it cannot offer attribute options of its own.");
        }

        var attribute = await db.VariantAttributes
            .AsNoTracking()
            .Where(x => x.OrganizationId == context.OrganizationId && x.Name == attributeName)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        // Phase 24 read live that attribute names are deliberately NOT unique -- the reference
        // tenant carries both "size" and "Size" -- so the index is non-unique and two rows can come
        // back. An ambiguous name is a row error naming the ambiguity rather than a coin flip, the
        // same choice ContactPersonnelImporter makes for its Organisation column.
        if (attribute.Count == 0)
        {
            throw new ImportRowException(
                ColumnAttribute,
                $"No variant attribute named '{attributeName}' exists in this organization.");
        }

        if (attribute.Count > 1)
        {
            throw new ImportRowException(
                ColumnAttribute,
                $"'{attributeName}' matches more than one variant attribute; rename one of them, or set this "
                    + "product's attributes on its own Variants tab.");
        }

        var attributeId = attribute[0].Id;

        var options = await db.VariantAttributeOptions
            .AsNoTracking()
            .Where(x => x.VariantAttributeId == attributeId && x.Value == optionValue)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (options.Count == 0)
        {
            throw new ImportRowException(
                ColumnValue, $"'{attributeName}' has no option '{optionValue}'.");
        }

        if (options.Count > 1)
        {
            throw new ImportRowException(
                ColumnValue, $"'{attributeName}' has more than one option called '{optionValue}'.");
        }

        var optionId = options[0];

        // The union, re-read per row. See the type doc comment for why this is an add rather than
        // the wholesale replace the command's name suggests.
        var existing = await db.ProductVariantAttributeUsages
            .AsNoTracking()
            .Where(x => x.ProductId == product.Id)
            .Select(x => new VariantCombinationInput(x.VariantAttributeId, x.VariantAttributeOptionId))
            .ToListAsync(cancellationToken);

        var usages = existing.Any(x => x.AttributeId == attributeId && x.OptionId == optionId)
            ? existing
            : [.. existing, new VariantCombinationInput(attributeId, optionId)];

        var action = existing.Count == usages.Count
            ? $"'{product.Name}' already offers {attributeName} {optionValue}"
            : $"Add {attributeName} {optionValue} to '{product.Name}'";

        return ImportRowPlan.For<SetProductVariantAttributesCommand, ProductVariantAttributesResult>(
            new SetProductVariantAttributesCommand(context.OrganizationId, product.Id, usages),
            action,
            product.Code,
            result => new ImportRowResult(result.ProductId, product.Code));
    }
}
