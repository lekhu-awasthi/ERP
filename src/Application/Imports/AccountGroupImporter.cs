using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports;

/// <summary>
/// Account Group bulk import (FR-2.9) -- the second and last of the upload types whose rows can
/// point at each other. Columns are the reference product's own template, read live in Phase 21a:
/// <b>Name</b>, Description, <b>Parent Group</b>, <b>Primary Group</b>.
///
/// <para><b>"Primary Group" is this codebase's <c>AccountRootType</c></b>, and it is required on
/// every row even though a child group's root is fixed by its parent. That is the reference
/// template's own shape, and honouring it buys a real check: a row whose Primary Group disagrees
/// with its parent's is a mistake the user can see and fix, whereas silently inheriting would import
/// a group the file says is an Expense into the Asset tree without a word.</para>
///
/// <para><c>Description</c> is absent -- <c>AccountGroup</c> has no such field.</para>
///
/// <para><b>Create-only</b>, matching the reference product (Phase 21a found it offers Create alone
/// for this type and Product Category). There is a second reason here that does not apply to
/// categories: <c>AccountGroup.Update</c> deliberately refuses to change <c>RootType</c> -- phase 3's
/// ruling that regrouping an Asset into a Liability is a modelling smell -- so an update file
/// carrying a Primary Group column could not honour half of what it says.</para>
/// </summary>
public sealed class AccountGroupImporter(IAppDbContext db) : IEntityImporter, IHierarchicalImporter
{
    private const string ColumnName = "Name";
    private const string ColumnParent = "Parent Group";
    private const string ColumnPrimaryGroup = "Primary Group";

    /// <summary>The five spellings are <c>AccountRootType</c>'s member names, which are also the
    /// words <c>POST /accounts</c> takes and the words the Chart of Accounts screen shows.</summary>
    private static readonly Dictionary<string, AccountRootType> RootTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asset"] = AccountRootType.Asset,
        ["Liability"] = AccountRootType.Liability,
        ["Equity"] = AccountRootType.Equity,
        ["Income"] = AccountRootType.Income,
        ["Expense"] = AccountRootType.Expense,
    };

    public ImportEntityType EntityType => ImportEntityType.AccountGroup;

    public string KeyColumn => ColumnName;

    public string ParentColumn => ColumnParent;

    public ImportTemplateDefinition Template { get; } = new(
        ImportEntityType.AccountGroup,
        SheetName: "Account Groups",
        FileNameStem: "AccountGroupImportTemplate",
        Columns:
        [
            new ImportColumn(ColumnName, Required: true),
            new ImportColumn(ColumnParent, Required: true),
            new ImportColumn(ColumnPrimaryGroup, Required: true),
        ],
        SampleRow: ["Indirect Expenses", "", "Expense"],
        Instructions:
        [
            "Instruction",
            "- ** marks a required field.",
            "- This upload creates account groups only; it never updates existing ones.",
            "- \"Primary Group\": Asset, Liability, Equity, Income or Expense.",
            "- \"Parent Group\" may name a group that already exists in this organization OR one",
            "  created by another row of this same file -- in either order. Leave the cell blank for a",
            "  top-level group (the column itself must stay).",
            "- A child group's Primary Group must match its parent's.",
            "- A file whose Parent Group references form a cycle is rejected whole: nothing is",
            "  imported, and the message names the rows involved.",
            "- Each Name must appear only once in the file.",
            "Note: Do not change the column headers.",
        ]);

    public async Task<ImportRowPlan> PlanAsync(
        ImportRowContext context, ImportRowReader row, CancellationToken cancellationToken)
    {
        if (context.Mode != ImportMode.CreateNew)
        {
            throw new ImportRowException(
                null, "Account groups can only be created by import, not updated. Re-upload with Create New Records.");
        }

        var name = row.GetRequiredString(ColumnName);
        var rootType = row.GetChoice(ColumnPrimaryGroup, RootTypes, required: true, AccountRootType.Asset);
        var parentName = row.GetOptionalString(ColumnParent);

        if (parentName is null)
        {
            return ImportRowPlan.For<CreateAccountGroupCommand, CreateAccountGroupResult>(
                new CreateAccountGroupCommand(context.OrganizationId, name, rootType, null),
                $"Create top-level {rootType} group '{name}'",
                targetCode: null,
                created => new ImportRowResult(created.Id, created.Name));
        }

        var parent = await db.AccountGroups
            .Where(x => x.OrganizationId == context.OrganizationId && x.Name == parentName)
            .Select(x => new { x.Id, x.RootType })
            .FirstOrDefaultAsync(cancellationToken);

        if (parent is null)
        {
            // Created by a later row of this same file: only reachable during the dry run, because
            // the apply pass runs in ImportRowSequencer's order. The Primary Group agreement check
            // below is therefore skipped for this row in the dry run and enforced when it applies --
            // stated here rather than left for a reader to notice.
            return context.WillCreate(parentName)
                ? ImportRowPlan.Provisional($"Create {rootType} group '{name}' under '{parentName}'")
                : throw new ImportRowException(
                    ColumnParent,
                    $"Parent group '{parentName}' is neither in this organization nor in this file.");
        }

        if (parent.RootType != rootType)
        {
            throw new ImportRowException(
                ColumnPrimaryGroup,
                $"'{name}' is declared {rootType} but its parent '{parentName}' is {parent.RootType}; "
                    + "a group cannot sit under a different primary group.");
        }

        return ImportRowPlan.For<CreateAccountGroupCommand, CreateAccountGroupResult>(
            new CreateAccountGroupCommand(context.OrganizationId, name, rootType, parent.Id),
            $"Create {rootType} group '{name}' under '{parentName}'",
            targetCode: null,
            created => new ImportRowResult(created.Id, created.Name));
    }
}
