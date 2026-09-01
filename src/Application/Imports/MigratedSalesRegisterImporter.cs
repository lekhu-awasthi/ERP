using ErpApp.Application.Sales.Commands.CreateMigratedSalesRegisterEntry;
using ErpApp.Domain.Imports;
using MediatR;

namespace ErpApp.Application.Imports;

/// <summary>
/// Migrated Sales Register import (FR-2.10). One spreadsheet row becomes one
/// <c>MigratedSalesRegisterEntry</c> through <c>CreateMigratedSalesRegisterEntryCommand</c> and the
/// normal MediatR pipeline -- see that command for Decision D.
///
/// <para><b>Decision E, stated honestly: these columns were not read off the reference product's own
/// migration template.</b> This phase's session was non-interactive, and CLAUDE.md's standing rule
/// is that the user signs in themselves, so <c>Configurations &gt; Organization &gt; Migration</c>
/// was never opened. What makes shipping a template anyway defensible here -- and did not make it
/// defensible in Phase 21a, where the Product template's columns had no independent derivation -- is
/// that this column set is not a guess at all: it is Phase 19's <b>live-confirmed, column-by-column
/// reading of the statutory Sales Register itself</b> (decision #3), which the migrated variant must
/// match by construction, since the entire point of the feature is that pre-cutover history appears
/// in the same statutory form as post-cutover activity. The residual risk is the reference product's
/// own header <i>wording</i> and its party-identification convention, not the data. A user filling
/// this app's own downloadable template is unaffected either way, because
/// <c>ImportTemplateDefinition</c> is the single declaration that drives both the file written out
/// and the headers parsed back in.</para>
///
/// <para><b>Four columns here have no live-register counterpart that can ever be populated.</b>
/// Export Value / Country / Declaration No / Declaration Date ship hardcoded to 0/null on the live
/// Sales Register because this codebase's Invoice has no export-sale flag (FR-5.8, deferred to Phase
/// 23). A migrated row has no such gap -- the prior system knew -- so they are accepted here. Four
/// columns is a small price for the only statutory data a cutover would otherwise lose outright.
/// </para>
/// </summary>
public sealed class MigratedSalesRegisterImporter(ISender sender) : IEntityImporter
{
    private const string ColumnDate = "Date";
    private const string ColumnDocumentNo = "Document No";
    private const string ColumnCustomerName = "Customer Name";
    private const string ColumnCustomerPan = "Customer PAN";
    private const string ColumnTotalValue = "Total Sales Value";
    private const string ColumnTaxExemptValue = "Tax-Exempt Sales Value";
    private const string ColumnTaxableValue = "Taxable Sales Value";
    private const string ColumnVatAmount = "VAT Amount";
    private const string ColumnExportValue = "Export Value";
    private const string ColumnExportCountry = "Export Country";
    private const string ColumnExportDeclarationNo = "Export Declaration No";
    private const string ColumnExportDeclarationDate = "Export Declaration Date";

    public ImportEntityType EntityType => ImportEntityType.MigratedSalesRegister;

    public ImportTemplateDefinition Template { get; } = new(
        ImportEntityType.MigratedSalesRegister,
        SheetName: "Migrated Sales Register",
        FileNameStem: "MigratedSalesRegisterTemplate",
        Columns:
        [
            new ImportColumn(ColumnDate, Required: true),
            new ImportColumn(ColumnDocumentNo, Required: true),
            new ImportColumn(ColumnCustomerName, Required: true),
            new ImportColumn(ColumnCustomerPan, Required: false),
            new ImportColumn(ColumnTotalValue, Required: true),
            new ImportColumn(ColumnTaxExemptValue, Required: false),
            new ImportColumn(ColumnTaxableValue, Required: false),
            new ImportColumn(ColumnVatAmount, Required: false),
            new ImportColumn(ColumnExportValue, Required: false),
            new ImportColumn(ColumnExportCountry, Required: false),
            new ImportColumn(ColumnExportDeclarationNo, Required: false),
            new ImportColumn(ColumnExportDeclarationDate, Required: false),
        ],
        SampleRow:
        [
            "2024-07-30", "INV-0912", "Himalayan Traders Private Limited", "301234567",
            "113000", "0", "100000", "13000", "0", null, null, null,
        ],
        Instructions:
        [
            "These rows are historical statutory data imported from your previous system at cutover.",
            "They appear ONLY in the Migrated Sales Register report. They are never posted to the "
                + "General Ledger, never become Invoices, and never affect any other report.",
            "Copy the values exactly as your previous system filed them, rounding included. Nothing "
                + "here is recalculated, and VAT is not cross-checked against the taxable value.",
            "A sales RETURN (credit note) is entered as a row with NEGATIVE values, the same way the "
                + "live Sales Register shows one. Do not use a separate document type column.",
            "Date accepts yyyy-MM-dd (AD). Enter the transaction date from the original document.",
            "Document No is your previous system's own invoice number. It must be unique -- "
                + "re-uploading a file that has already been imported rejects every repeated row.",
            "Customer PAN is optional. When it exactly matches a Contact already in this "
                + "organization, the row is linked to it; otherwise the name and PAN stand alone. "
                + "No Contact is ever created by this import.",
            "The four Export columns are optional and apply only to export sales.",
            "Do not change the column headers.",
        ]);

    public async Task<ImportRowResult> ApplyAsync(
        Guid organizationId, ImportMode mode, ImportRowReader row, CancellationToken cancellationToken)
    {
        // Defence in depth only -- CreateImportJobCommandValidator already rejects UpdateExisting for
        // this entity type at upload time, so this cannot be reached through the UI. It is here
        // because "silently create when asked to update" is the worst possible reading of an
        // ambiguous request, and a job enqueued by some future caller must not get it.
        if (mode != ImportMode.CreateNew)
        {
            throw new ImportRowException(
                null, "Migrated register rows can only be created, not updated. Re-upload with Create New Records.");
        }

        var result = await sender.Send(
            new CreateMigratedSalesRegisterEntryCommand(
                organizationId,
                row.GetRequiredDate(ColumnDate),
                row.GetRequiredString(ColumnDocumentNo),
                row.GetRequiredString(ColumnCustomerName),
                row.GetOptionalString(ColumnCustomerPan),
                row.GetOptionalDecimal(ColumnTotalValue),
                row.GetOptionalDecimal(ColumnTaxExemptValue),
                row.GetOptionalDecimal(ColumnTaxableValue),
                row.GetOptionalDecimal(ColumnVatAmount),
                row.GetOptionalDecimal(ColumnExportValue),
                row.GetOptionalString(ColumnExportCountry),
                row.GetOptionalString(ColumnExportDeclarationNo),
                row.GetOptionalDate(ColumnExportDeclarationDate)),
            cancellationToken);

        return new ImportRowResult(result.Id, result.DocumentCode);
    }
}
