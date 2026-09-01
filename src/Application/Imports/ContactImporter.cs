using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Contacts.Commands.UpdateContact;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Imports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports;

/// <summary>
/// Customer and Supplier bulk import (FR-2.9) -- <b>one importer, two registrations</b>, because
/// <c>Contact</c> is a single aggregate discriminated by <c>ContactType</c> and the reference
/// product's Customer and Supplier templates are byte-for-byte the same shape apart from the word
/// in the name column. Registering the same class twice with different
/// <see cref="ContactImporter.EntityType"/> values keeps the wizard's two upload types distinct to
/// the user while sharing one code path.
///
/// <para><b>Two of the reference template's columns are deliberately absent:</b> <c>Credit Limit</c>
/// and <c>Credit Term</c>. This codebase's <c>Contact</c> has neither field (<c>CreditTerm</c>
/// exists as a Configuration lookup but nothing on Contact references it), so both columns would
/// have been silently discarded.</para>
///
/// <para><b><c>Opening Balance Type</c> is absent for a different and more interesting reason.</b>
/// The reference template pairs an amount with a DR/CR word. Here <c>Contact.OpeningBalance</c> is a
/// single non-negative decimal -- <c>CreateContactCommandValidator</c> enforces
/// <c>GreaterThanOrEqualTo(0)</c> -- whose side is <i>derived</i> from <c>ContactType</c> by
/// <c>ContactLedgerReader.BalanceType</c> (a Customer's balance is a receivable, a Supplier's a
/// payable). A DR/CR column would therefore be either redundant with the upload type or, when it
/// disagreed with it, unrepresentable. Accepting a column the model cannot honour is worse than not
/// offering it.</para>
/// </summary>
public sealed class ContactImporter : IEntityImporter
{
    private const string ColumnCode = "Code";
    private const string ColumnContactGroup = "Contact Group";
    private const string ColumnPhone = "Phone No";
    private const string ColumnEmail = "Email";
    private const string ColumnAddress = "Address";
    private const string ColumnPan = "PAN";
    private const string ColumnOpeningBalance = "Opening Balance";

    private readonly IAppDbContext _db;
    private readonly ISender _sender;
    private readonly ContactType _contactType;
    private readonly string _columnName;

    private ContactImporter(IAppDbContext db, ISender sender, ImportEntityType entityType, ContactType contactType)
    {
        _db = db;
        _sender = sender;
        _contactType = contactType;
        _columnName = $"{contactType} Name";
        EntityType = entityType;
        Template = BuildTemplate(entityType, contactType, _columnName);
    }

    public static ContactImporter ForCustomers(IAppDbContext db, ISender sender) =>
        new(db, sender, ImportEntityType.Customer, ContactType.Customer);

    public static ContactImporter ForSuppliers(IAppDbContext db, ISender sender) =>
        new(db, sender, ImportEntityType.Supplier, ContactType.Supplier);

    public ImportEntityType EntityType { get; }

    public ImportTemplateDefinition Template { get; }

    public async Task<ImportRowResult> ApplyAsync(
        Guid organizationId, ImportMode mode, ImportRowReader row, CancellationToken cancellationToken)
    {
        var name = row.GetRequiredString(_columnName);
        var groupId = await ResolveGroupAsync(organizationId, row, cancellationToken);
        var address = row.GetOptionalString(ColumnAddress);
        var pan = row.GetOptionalString(ColumnPan);
        var phone = row.GetOptionalString(ColumnPhone);
        var email = row.GetOptionalString(ColumnEmail);
        var openingBalance = row.GetOptionalDecimal(ColumnOpeningBalance);

        if (mode == ImportMode.CreateNew)
        {
            var created = await _sender.Send(
                new CreateContactCommand(
                    organizationId, _contactType, name, address, pan, phone, email, groupId, openingBalance),
                cancellationToken);

            return new ImportRowResult(created.Id, created.Code);
        }

        var existing = await FindByCodeAsync(organizationId, row, cancellationToken);

        // ContactType is immutable (see its doc comment), so a Supplier import must not be able to
        // reach a Customer row by code. This is also the check that keeps the two upload types
        // honest: without it, "Supplier" and "Customer" would be the same import with a label.
        if (existing.Type != _contactType)
        {
            throw new ImportRowException(
                ColumnCode,
                $"'{existing.Code}' is a {existing.Type}, not a {_contactType}; contact type cannot be changed by import.");
        }

        var updated = await _sender.Send(
            new UpdateContactCommand(
                organizationId, existing.Id, name, address, pan, phone, email, groupId, openingBalance),
            cancellationToken);

        return new ImportRowResult(updated.Id, existing.Code);
    }

    private static ImportTemplateDefinition BuildTemplate(
        ImportEntityType entityType, ContactType contactType, string nameColumn) =>
        new(
            entityType,
            SheetName: $"{contactType}s",
            FileNameStem: $"{contactType}ImportTemplate",
            Columns:
            [
                new ImportColumn(ColumnCode, Required: false),
                new ImportColumn(nameColumn, Required: true),
                new ImportColumn(ColumnContactGroup, Required: false),
                new ImportColumn(ColumnPhone, Required: false),
                new ImportColumn(ColumnEmail, Required: false),
                new ImportColumn(ColumnAddress, Required: false),
                new ImportColumn(ColumnPan, Required: false),
                new ImportColumn(ColumnOpeningBalance, Required: false),
            ],
            SampleRow:
            [
                string.Empty,
                "Kathmandu Trading Concern",
                string.Empty,
                "9841768644",
                "accounts@example.com",
                "Kathmandu-32, Nepal",
                "304567847",
                "0",
            ],
            Instructions:
            [
                "Instruction",
                "- ** marks a required field.",
                "- Leave Code blank when creating: it is generated automatically.",
                "- In Update Existing Records mode, Code is required and must match an existing record.",
                "- \"Contact Group\" must exactly match a Contact Group name already in this organization, or be left blank.",
                $"- Opening Balance is a positive amount; it is treated as a {(contactType == ContactType.Customer ? "receivable (DR)" : "payable (CR)")} because this is a {contactType} import.",
                "Note: Do not change the column headers.",
            ]);

    private async Task<Guid?> ResolveGroupAsync(
        Guid organizationId, ImportRowReader row, CancellationToken cancellationToken)
    {
        var groupName = row.GetOptionalString(ColumnContactGroup);
        if (groupName is null)
        {
            return null;
        }

        var groupId = await _db.ContactGroups
            .Where(x => x.OrganizationId == organizationId && x.Name == groupName)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return groupId ?? throw new ImportRowException(
            ColumnContactGroup, $"Contact group '{groupName}' does not exist in this organization.");
    }

    private async Task<Contact> FindByCodeAsync(
        Guid organizationId, ImportRowReader row, CancellationToken cancellationToken)
    {
        var code = row.GetOptionalString(ColumnCode)
            ?? throw new ImportRowException(
                ColumnCode, $"'{ColumnCode}' is required when updating existing records.");

        var contact = await _db.Contacts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Code == code, cancellationToken);

        return contact ?? throw new ImportRowException(
            ColumnCode, $"No record with code '{code}' exists in this organization.");
    }
}
