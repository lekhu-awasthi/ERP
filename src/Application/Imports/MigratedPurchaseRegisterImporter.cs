using ErpApp.Application.Purchasing.Commands.CreateMigratedPurchaseRegisterEntry;
using ErpApp.Domain.Imports;
using MediatR;

namespace ErpApp.Application.Imports;

/// <summary>
/// Migrated Purchase Register import (FR-2.10). The Purchase-side twin of
/// <see cref="MigratedSalesRegisterImporter"/> -- read that class for Decision E (why a template
/// derived from Phase 19's live-confirmed register columns is defensible where Phase 21a's would not
/// have been).
///
/// <para><b>The Capital / Non-Capital and Local / Import splits are separate columns, not a
/// classification column.</b> This mirrors the statutory register itself: the IRD Purchase Book
/// prints three value/VAT pairs side by side, and a prior system's export of it therefore already
/// carries them apportioned. Offering a single "classification" word plus one amount would have
/// forced every row to be wholly capital or wholly local, which a real bill with mixed lines is
/// not -- and this codebase's own live Purchase Register apportions per <i>line</i>
/// (<c>ExpenditureClassification</c>, Phase 8e), so collapsing a migrated row to one bucket would
/// make the two registers structurally incomparable.</para>
/// </summary>
public sealed class MigratedPurchaseRegisterImporter(ISender sender) : IEntityImporter
{
    private const string ColumnDate = "Date";
    private const string ColumnDocumentNo = "Bill No";
    private const string ColumnImportDeclarationNo = "Import Declaration No";
    private const string ColumnSupplierName = "Supplier Name";
    private const string ColumnSupplierPan = "Supplier PAN";
    private const string ColumnTaxExemptValue = "Tax-Exempt Value";
    private const string ColumnNonCapitalLocalValue = "Taxable Non-Capital (Local) Value";
    private const string ColumnNonCapitalLocalVat = "Taxable Non-Capital (Local) VAT";
    private const string ColumnNonCapitalImportValue = "Taxable Non-Capital (Import) Value";
    private const string ColumnNonCapitalImportVat = "Taxable Non-Capital (Import) VAT";
    private const string ColumnCapitalValue = "Taxable Capital Value";
    private const string ColumnCapitalVat = "Taxable Capital VAT";

    public ImportEntityType EntityType => ImportEntityType.MigratedPurchaseRegister;

    public ImportTemplateDefinition Template { get; } = new(
        ImportEntityType.MigratedPurchaseRegister,
        SheetName: "Migrated Purchase Register",
        FileNameStem: "MigratedPurchaseRegisterTemplate",
        Columns:
        [
            new ImportColumn(ColumnDate, Required: true),
            new ImportColumn(ColumnDocumentNo, Required: true),
            new ImportColumn(ColumnImportDeclarationNo, Required: false),
            new ImportColumn(ColumnSupplierName, Required: true),
            new ImportColumn(ColumnSupplierPan, Required: false),
            new ImportColumn(ColumnTaxExemptValue, Required: false),
            new ImportColumn(ColumnNonCapitalLocalValue, Required: false),
            new ImportColumn(ColumnNonCapitalLocalVat, Required: false),
            new ImportColumn(ColumnNonCapitalImportValue, Required: false),
            new ImportColumn(ColumnNonCapitalImportVat, Required: false),
            new ImportColumn(ColumnCapitalValue, Required: false),
            new ImportColumn(ColumnCapitalVat, Required: false),
        ],
        SampleRow:
        [
            "2024-07-28", "BILL-4471", null, "Everest Supplies Private Limited", "302345678",
            "0", "80000", "10400", "0", "0", "0", "0",
        ],
        Instructions:
        [
            "These rows are historical statutory data imported from your previous system at cutover.",
            "They appear ONLY in the Migrated Purchase Register report. They are never posted to the "
                + "General Ledger, never become Purchase Bills, and never affect any other report.",
            "Copy the values exactly as your previous system filed them, rounding included. Nothing "
                + "here is recalculated, and VAT is not cross-checked against the taxable value.",
            "A purchase RETURN (debit note) is entered as a row with NEGATIVE values, the same way "
                + "the live Purchase Register shows one.",
            "Date accepts yyyy-MM-dd (AD). Enter the transaction date from the original document.",
            "Bill No is your previous system's own bill number. It must be unique -- re-uploading a "
                + "file that has already been imported rejects every repeated row.",
            "Split each bill's taxable value across the three pairs exactly as the statutory Purchase "
                + "Book does: Non-Capital (Local), Non-Capital (Import), and Capital. Leave a pair at "
                + "0 where it does not apply.",
            "Import Declaration No is the customs Pragyapan Patra number, for imported purchases only.",
            "Supplier PAN is optional. When it exactly matches a Contact already in this "
                + "organization, the row is linked to it. No Contact is ever created by this import.",
            "Do not change the column headers.",
        ]);

    public async Task<ImportRowResult> ApplyAsync(
        Guid organizationId, ImportMode mode, ImportRowReader row, CancellationToken cancellationToken)
    {
        // Defence in depth -- CreateImportJobCommandValidator rejects UpdateExisting for this type at
        // upload time. See the Sales-side importer.
        if (mode != ImportMode.CreateNew)
        {
            throw new ImportRowException(
                null, "Migrated register rows can only be created, not updated. Re-upload with Create New Records.");
        }

        var result = await sender.Send(
            new CreateMigratedPurchaseRegisterEntryCommand(
                organizationId,
                row.GetRequiredDate(ColumnDate),
                row.GetRequiredString(ColumnDocumentNo),
                row.GetOptionalString(ColumnImportDeclarationNo),
                row.GetRequiredString(ColumnSupplierName),
                row.GetOptionalString(ColumnSupplierPan),
                row.GetOptionalDecimal(ColumnTaxExemptValue),
                row.GetOptionalDecimal(ColumnNonCapitalLocalValue),
                row.GetOptionalDecimal(ColumnNonCapitalLocalVat),
                row.GetOptionalDecimal(ColumnNonCapitalImportValue),
                row.GetOptionalDecimal(ColumnNonCapitalImportVat),
                row.GetOptionalDecimal(ColumnCapitalValue),
                row.GetOptionalDecimal(ColumnCapitalVat)),
            cancellationToken);

        return new ImportRowResult(result.Id, result.DocumentCode);
    }
}
