using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports;

/// <summary>
/// Product Category bulk import (FR-2.9) -- one of the two upload types whose rows can point at
/// <b>each other</b>. Columns are the reference product's own template, read live in Phase 21a:
/// <b>Category Name</b>, Parent Category, Description.
///
/// <para><b>Create-only, matching the reference product</b>, which offers "Create New Records" only
/// for this type and Account Group while offering both modes for the other five. Phase 21a recorded
/// that asymmetry as "a real asymmetry, not an oversight, and worth honouring if those two are ever
/// built here"; this is that. It is also the only sensible reading: a category's identity is its
/// name, so an update file would have nothing to match on.</para>
///
/// <para><c>Description</c> is absent -- <c>ProductCategory</c> has no such field.</para>
///
/// <para><b>The parent may appear anywhere in the file, including below the row that names it.</b>
/// That is not this importer's problem: <see cref="ImportRowSequencer"/> orders the whole file
/// before the first row is planned, so by the time <see cref="PlanAsync"/> runs, a parent that is in
/// the file has already been created and resolves by name exactly like one that was already in the
/// database. The importer therefore cannot tell the two apart, and does not need to.</para>
/// </summary>
public sealed class ProductCategoryImporter(IAppDbContext db) : IEntityImporter, IHierarchicalImporter
{
    private const string ColumnName = "Category Name";
    private const string ColumnParent = "Parent Category";

    public ImportEntityType EntityType => ImportEntityType.ProductCategory;

    public string KeyColumn => ColumnName;

    public string ParentColumn => ColumnParent;

    public ImportTemplateDefinition Template { get; } = new(
        ImportEntityType.ProductCategory,
        SheetName: "Product Categories",
        FileNameStem: "ProductCategoryImportTemplate",
        Columns:
        [
            new ImportColumn(ColumnName, Required: true),
            new ImportColumn(ColumnParent, Required: false),
        ],
        SampleRow: ["Snacks", "Food"],
        Instructions:
        [
            "Instruction",
            "- ** marks a required field.",
            "- This upload creates categories only; it never updates existing ones.",
            "- \"Parent Category\" may name a category that already exists in this organization OR",
            "  one created by another row of this same file -- in either order. Leave it blank for a",
            "  top-level category.",
            "- A file whose parent references form a cycle is rejected whole: nothing is imported,",
            "  and the message names the rows involved.",
            "- Each Category Name must appear only once in the file.",
            "Note: Do not change the column headers.",
        ]);

    public async Task<ImportRowPlan> PlanAsync(
        ImportRowContext context, ImportRowReader row, CancellationToken cancellationToken)
    {
        // Defence in depth: CreateImportJobCommandValidator rejects UpdateExisting for this type at
        // upload time, so this is unreachable through the UI. See MigratedSalesRegisterImporter for
        // why "silently create when asked to update" is the one reading never to allow.
        if (context.Mode != ImportMode.CreateNew)
        {
            throw new ImportRowException(
                null, "Product categories can only be created by import, not updated. Re-upload with Create New Records.");
        }

        var name = row.GetRequiredString(ColumnName);
        var parentName = row.GetOptionalString(ColumnParent);
        var parentId = await ResolveParentAsync(context.OrganizationId, parentName, cancellationToken);

        if (parentId is null && parentName is not null)
        {
            // The parent is created by a row of this same file that has not run yet, so this can
            // only be the dry run -- during the apply pass ImportRowSequencer guarantees it already
            // exists. See ImportRowPlan.Provisional.
            return context.WillCreate(parentName)
                ? ImportRowPlan.Provisional($"Create category '{name}' under '{parentName}'")
                : throw new ImportRowException(
                    ColumnParent,
                    $"Parent category '{parentName}' is neither in this organization nor in this file.");
        }

        return ImportRowPlan.For<CreateProductCategoryCommand, CreateProductCategoryResult>(
            new CreateProductCategoryCommand(context.OrganizationId, name, parentId),
            parentName is null ? $"Create top-level category '{name}'" : $"Create category '{name}' under '{parentName}'",
            targetCode: null,
            created => new ImportRowResult(created.Id, created.Name));
    }

    private async Task<Guid?> ResolveParentAsync(
        Guid organizationId, string? parentName, CancellationToken cancellationToken)
    {
        if (parentName is null)
        {
            return null;
        }

        return await db.ProductCategories
            .Where(x => x.OrganizationId == organizationId && x.Name == parentName)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
