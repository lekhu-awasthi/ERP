using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContactPersonnel;
using ErpApp.Application.Contacts.Commands.UpdateContactPersonnel;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports;

/// <summary>
/// Contact Personnel bulk import (FR-2.9, FR-4.5) -- the reference product's <c>Contact</c> upload
/// type, whose columns are Code, <b>Contact Name</b>, Contact Group, Phone No, Email, Address,
/// <b>Organisation</b>, <b>Title</b>.
///
/// <para><b>This is the importer Phase 21a's confirm-live pass was written to prevent getting
/// wrong.</b> The kickoff for that phase assumed "Customers, Suppliers, Contacts" was one importer
/// over <c>ContactType</c>; the live template's instruction -- <i>"'Organisation' should exactly
/// match with customer or supplier name in the existing contact list"</i> -- showed that its
/// "Contact" is a <b>person attached to</b> a Customer or Supplier, which is
/// <c>ContactPersonnel</c> here and a different aggregate entirely. 21a deferred it rather than
/// half-build it against the wrong type; this is the deferred half.</para>
///
/// <para><b>Update mode matches on Organisation + Code, and says so, because Code is not unique
/// here.</b> <c>ContactPersonnel.Code</c> is a nullable free-text field with no unique index -- two
/// people under different parents may legitimately carry the same one, and even two under the
/// <i>same</i> parent are not prevented. So a row is matched inside its own Organisation, and an
/// ambiguous match is a row error naming the ambiguity rather than a coin flip. That is weaker than
/// the natural key the other importers get, and pretending otherwise would silently overwrite the
/// wrong person.</para>
/// </summary>
public sealed class ContactPersonnelImporter(IAppDbContext db) : IEntityImporter
{
    private const string ColumnCode = "Code";
    private const string ColumnName = "Contact Name";
    private const string ColumnGroup = "Contact Group";
    private const string ColumnPhone = "Phone No";
    private const string ColumnEmail = "Email";
    private const string ColumnAddress = "Address";
    private const string ColumnOrganisation = "Organisation";
    private const string ColumnTitle = "Title";

    public ImportEntityType EntityType => ImportEntityType.ContactPersonnel;

    public ImportTemplateDefinition Template { get; } = new(
        ImportEntityType.ContactPersonnel,
        SheetName: "Contact Personnel",
        FileNameStem: "ContactPersonnelImportTemplate",
        Columns:
        [
            new ImportColumn(ColumnCode, Required: false),
            new ImportColumn(ColumnName, Required: true),
            new ImportColumn(ColumnGroup, Required: false),
            new ImportColumn(ColumnPhone, Required: false),
            new ImportColumn(ColumnEmail, Required: false),
            new ImportColumn(ColumnAddress, Required: false),
            new ImportColumn(ColumnOrganisation, Required: true),
            new ImportColumn(ColumnTitle, Required: true),
        ],
        SampleRow:
        [
            string.Empty,
            "Sita Sharma",
            string.Empty,
            "9841768644",
            "sita@example.com",
            "Kathmandu-32, Nepal",
            "Kathmandu Trading Concern",
            "Accounts Manager",
        ],
        Instructions:
        [
            "Instruction",
            "- ** marks a required field.",
            "- \"Organisation\" must exactly match a Customer or Supplier name already in this",
            "  organization: it is the contact this person belongs to.",
            "- \"Contact Group\" must exactly match a Contact Group name already in this organization,",
            "  or be left blank.",
            "- \"Title\" is the person's role, for example \"Accounts Manager\".",
            "- In Update Existing Records mode, Code is required and is matched within the named",
            "  Organisation. Codes are not unique here, so a code matching two people under the same",
            "  Organisation is reported rather than guessed at.",
            "Note: Do not change the column headers.",
        ]);

    public async Task<ImportRowPlan> PlanAsync(
        ImportRowContext context, ImportRowReader row, CancellationToken cancellationToken)
    {
        var name = row.GetRequiredString(ColumnName);
        var title = row.GetRequiredString(ColumnTitle);
        var parent = await ResolveOrganisationAsync(context.OrganizationId, row, cancellationToken);
        var groupId = await ResolveGroupAsync(context.OrganizationId, row, cancellationToken);
        var code = row.GetOptionalString(ColumnCode);
        var phone = row.GetOptionalString(ColumnPhone);
        var email = row.GetOptionalString(ColumnEmail);
        var address = row.GetOptionalString(ColumnAddress);

        if (context.Mode == ImportMode.CreateNew)
        {
            return ImportRowPlan.For<CreateContactPersonnelCommand, ContactPersonnelResult>(
                new CreateContactPersonnelCommand(
                    context.OrganizationId, parent.Id, name, address, code, phone, groupId, email, title),
                $"Add '{name}' ({title}) to {parent.Name}",
                code,
                created => new ImportRowResult(created.Id, created.Code));
        }

        var existing = await FindExistingAsync(context.OrganizationId, parent, code, cancellationToken);

        return ImportRowPlan.For<UpdateContactPersonnelCommand, ContactPersonnelResult>(
            new UpdateContactPersonnelCommand(
                context.OrganizationId, parent.Id, existing.Id, name, address, code, phone, groupId, email, title),
            $"Update '{existing.Name}' under {parent.Name} to '{name}' ({title})",
            code,
            updated => new ImportRowResult(updated.Id, updated.Code));
    }

    private async Task<ContactRef> ResolveOrganisationAsync(
        Guid organizationId, ImportRowReader row, CancellationToken cancellationToken)
    {
        var organisationName = row.GetRequiredString(ColumnOrganisation);

        var matches = await db.Contacts
            .Where(x => x.OrganizationId == organizationId && x.Name == organisationName)
            .Select(x => new ContactRef(x.Id, x.Name, x.Type))
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            throw new ImportRowException(
                ColumnOrganisation,
                $"'{organisationName}' does not match any customer or supplier in this organization.");
        }

        if (matches.Count > 1)
        {
            throw new ImportRowException(
                ColumnOrganisation,
                $"'{organisationName}' matches more than one contact in this organization; "
                    + "rename one of them or import this person from the contact's own screen.");
        }

        // A Lead has no personnel tab in this product and the reference template says "customer or
        // supplier", so a Lead is rejected by name rather than quietly accepted.
        if (matches[0].Type == ContactType.Lead)
        {
            throw new ImportRowException(
                ColumnOrganisation,
                $"'{organisationName}' is a Lead; contact personnel belong to a customer or a supplier.");
        }

        return matches[0];
    }

    private async Task<Guid?> ResolveGroupAsync(
        Guid organizationId, ImportRowReader row, CancellationToken cancellationToken)
    {
        var groupName = row.GetOptionalString(ColumnGroup);
        if (groupName is null)
        {
            return null;
        }

        var groupId = await db.ContactGroups
            .Where(x => x.OrganizationId == organizationId && x.Name == groupName)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return groupId ?? throw new ImportRowException(
            ColumnGroup, $"Contact group '{groupName}' does not exist in this organization.");
    }

    private async Task<ContactPersonnel> FindExistingAsync(
        Guid organizationId, ContactRef parent, string? code, CancellationToken cancellationToken)
    {
        if (code is null)
        {
            throw new ImportRowException(
                ColumnCode, $"'{ColumnCode}' is required when updating existing records.");
        }

        var matches = await db.ContactPersonnel
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ContactId == parent.Id && x.Code == code)
            .Take(2)
            .ToListAsync(cancellationToken);

        return matches.Count switch
        {
            0 => throw new ImportRowException(
                ColumnCode, $"No person with code '{code}' exists under {parent.Name}."),
            1 => matches[0],
            _ => throw new ImportRowException(
                ColumnCode,
                $"More than one person under {parent.Name} carries code '{code}', so this row would be "
                    + "ambiguous. Give them distinct codes first."),
        };
    }

    private sealed record ContactRef(Guid Id, string Name, ContactType Type);
}
