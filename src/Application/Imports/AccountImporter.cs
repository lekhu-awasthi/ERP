using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.UpdateAccount;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports;

/// <summary>
/// Chart-of-Accounts leaf bulk import (FR-2.9), the fourth of the reference product's seven upload
/// types. Its columns are that product's own <c>Account</c> template, read live in Phase 21a:
/// Code, <b>Account Name</b>, <b>Account Group</b>, Opening Balance, Opening Balance Type,
/// Description.
///
/// <para><b>This importer looks hierarchical and is not, which is why it does not implement
/// <see cref="IHierarchicalImporter"/>.</b> The roadmap's phase-38 entry groups Account with
/// Product Category and Account Group as "trees", but an <c>Account</c> is a leaf: its "Account
/// Group" column points at an <c>AccountGroup</c>, a <i>different aggregate</i> that this file
/// cannot contain and that must already exist in the tenant. There are no edges inside an Account
/// file, so there is no order to compute and no cycle to detect -- and giving it the sequencer
/// anyway would have implied a relationship the model does not have.</para>
///
/// <para><b>Three of the reference template's columns are absent, each for a reason this codebase
/// has established before:</b>
/// <list type="bullet">
/// <item><c>Description</c> -- <c>Account</c> has no such field, and shipping a column that silently
/// does nothing is the failure <c>ProductImporter</c>'s doc comment names.</item>
/// <item><c>Opening Balance</c> / <c>Opening Balance Type</c> -- an account's opening balance here is
/// an <c>OpeningBalanceLine</c> (Phase 17): a dated "day zero" transaction with its own screen, its
/// own approval and its own GL consequences, not an attribute of the account. This is exactly the
/// reasoning that kept <c>Opening Quantity</c>/<c>Opening Rate</c> off the Product template, and it
/// applies harder here, because the DR/CR word the reference template pairs with the amount is
/// meaningful only inside a balanced opening entry that one row cannot express.</item>
/// </list></para>
///
/// <para><c>Kind</c> (Other/Bank/Cash) is not on the reference template either, so it is not offered:
/// a bank account also needs a linked institution and an account number to be worth anything, and
/// Phase 17 gave it its own screen. Every imported account is <c>AccountKind.Other</c>, and update
/// mode reads the existing value back rather than resetting it.</para>
/// </summary>
public sealed class AccountImporter(IAppDbContext db) : IEntityImporter
{
    private const string ColumnCode = "Code";
    private const string ColumnName = "Account Name";
    private const string ColumnGroup = "Account Group";

    public ImportEntityType EntityType => ImportEntityType.Account;

    public ImportTemplateDefinition Template { get; } = new(
        ImportEntityType.Account,
        SheetName: "Accounts",
        FileNameStem: "AccountImportTemplate",
        Columns:
        [
            new ImportColumn(ColumnCode, Required: false),
            new ImportColumn(ColumnName, Required: true),
            new ImportColumn(ColumnGroup, Required: true),
        ],
        SampleRow: ["", "Office Rent", "Indirect Expenses"],
        Instructions:
        [
            "Instruction",
            "- ** marks a required field.",
            "- Leave Code blank when creating: it is generated automatically.",
            "- In Update Existing Records mode, Code is required and must match an existing account.",
            "- \"Account Group\" must exactly match an Account Group name already in this organization.",
            "- An account's root type (Asset / Liability / Equity / Income / Expense) comes from its",
            "  group, so there is no column for it.",
            "- Opening balances are not set here: use Accounting > Opening Balances, which posts a",
            "  dated, balanced opening entry rather than stamping a number onto the account.",
            "Note: Do not change the column headers.",
        ]);

    public async Task<ImportRowPlan> PlanAsync(
        ImportRowContext context, ImportRowReader row, CancellationToken cancellationToken)
    {
        var name = row.GetRequiredString(ColumnName);
        var groupId = await ResolveGroupAsync(context.OrganizationId, row, cancellationToken);

        if (context.Mode == ImportMode.CreateNew)
        {
            return ImportRowPlan.For<CreateAccountCommand, CreateAccountResult>(
                new CreateAccountCommand(context.OrganizationId, name, groupId),
                $"Create account '{name}'",
                targetCode: null,
                created => new ImportRowResult(created.Id, created.Code));
        }

        var existing = await FindByCodeAsync(context.OrganizationId, row, cancellationToken);

        return ImportRowPlan.For<UpdateAccountCommand, UpdateAccountResult>(
            new UpdateAccountCommand(
                context.OrganizationId,
                existing.Id,
                name,
                groupId,
                // Import neither deactivates an account nor rewrites its Bank/Cash identity: none of
                // the three has a template column, so all three are read back and passed through.
                // UpdateAccountCommandHandler applies them unconditionally, so omitting them would
                // quietly turn a bank account into an ordinary one.
                IsActive: existing.IsActive,
                existing.Kind,
                existing.BankId,
                existing.AccountNumber),
            $"Update account '{existing.Code}' to '{name}'",
            existing.Code,
            updated => new ImportRowResult(updated.Id, existing.Code));
    }

    private async Task<Guid> ResolveGroupAsync(
        Guid organizationId, ImportRowReader row, CancellationToken cancellationToken)
    {
        var groupName = row.GetRequiredString(ColumnGroup);

        var groupId = await db.AccountGroups
            .Where(x => x.OrganizationId == organizationId && x.Name == groupName)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return groupId ?? throw new ImportRowException(
            ColumnGroup, $"Account group '{groupName}' does not exist in this organization.");
    }

    private async Task<Account> FindByCodeAsync(
        Guid organizationId, ImportRowReader row, CancellationToken cancellationToken)
    {
        var code = row.GetOptionalString(ColumnCode)
            ?? throw new ImportRowException(
                ColumnCode, $"'{ColumnCode}' is required when updating existing records.");

        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Code == code, cancellationToken);

        return account ?? throw new ImportRowException(
            ColumnCode, $"No account with code '{code}' exists in this organization.");
    }
}
