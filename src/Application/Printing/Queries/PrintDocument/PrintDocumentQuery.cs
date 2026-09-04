using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Printing.Queries.PrintDocument;

/// <summary>
/// Phase 20d's print pipeline (FR-11.2, closes Phase 16c's deferred print-formatted output),
/// completed across every transactional document type in Phase 27b.
///
/// Returns a plain DTO, not PDF bytes -- QuestPDF rendering is an Api-layer concern (see
/// ErpApp.Api.Printing.DocumentPdfRenderer), keeping this Application-layer handler free of a
/// rendering-library dependency, the same split ReportSpreadsheetExporter's callers use for
/// ClosedXML (Phase 16c).
///
/// No new PermissionKeys.* entry -- printing a document rides on that DocumentType's own existing
/// View permission (PrintDocumentPermissions.ViewPermissionFor), the same "no new key" reasoning
/// SetCustomStatusCommand/SetTransactionReportingTagsCommand used for their own document-scoped
/// actions.
///
/// <para><b>Phase 27b wired the remaining 9 types</b>, taking this from 6 of 15 to all 15.
/// Confirm-live (2026-09-03, docs/phase-27b-status.md Step 1) opened every one of the nine on the
/// reference tenant and found "View Print Preview" present on all of them -- including both
/// production documents, which the roadmap had flagged as unconfirmed. There is no gating: print is
/// universal, exactly as phase-20d's narrower sample suggested. <c>DocumentMechanisms.Printable</c>
/// is the classification, and a guard test fails the build if a transactional type is left
/// unwired.</para>
/// </summary>
public sealed record PrintDocumentQuery(Guid OrganizationId, DocumentType DocumentType, Guid DocumentId)
    : IRequest<PrintableDocumentDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PrintDocumentPermissions.ViewPermissionFor(DocumentType);
}

/// <summary>Each document type's own View key -- printing never widens what a role may see. The
/// switch still throws for a non-transactional <see cref="DocumentType"/> (numbering-pool stubs,
/// audit markers): those have no record to print, and a silent 403 would be a worse answer than a
/// loud one.</summary>
public static class PrintDocumentPermissions
{
    public static string ViewPermissionFor(DocumentType documentType) => documentType switch
    {
        DocumentType.Quotation => PermissionKeys.QuotationView,
        DocumentType.SalesOrder => PermissionKeys.SalesOrderView,
        DocumentType.Invoice => PermissionKeys.InvoiceView,
        DocumentType.CreditNote => PermissionKeys.CreditNoteView,
        DocumentType.Payment => PermissionKeys.PaymentView,
        DocumentType.PurchaseOrder => PermissionKeys.PurchaseOrderView,
        DocumentType.PurchaseBill => PermissionKeys.PurchaseBillView,
        DocumentType.Expense => PermissionKeys.ExpenseView,
        DocumentType.DebitNote => PermissionKeys.DebitNoteView,
        DocumentType.JournalVoucher => PermissionKeys.JournalVoucherView,
        DocumentType.CashTransfer => PermissionKeys.CashTransferView,
        DocumentType.WarehouseTransfer => PermissionKeys.WarehouseTransferView,
        DocumentType.InventoryAdjustment => PermissionKeys.InventoryAdjustmentView,
        DocumentType.ProductionOrder => PermissionKeys.ProductionOrderView,
        DocumentType.ProductionJournal => PermissionKeys.ProductionJournalView,
        _ => throw new ArgumentOutOfRangeException(
            nameof(documentType), documentType, "This document type has no printable record."),
    };
}

/// <summary>
/// One print shape for every document type, replacing phase-20d's "Lines XOR GlLines" pair.
///
/// <para><b>Why the shape changed in Phase 27b.</b> The confirm-live pass read the real print
/// output for a Production Journal, a Cash Transfer and a Customer Payment side by side. The page
/// <i>frame</i> is identical on all three -- organization block, document title, a short
/// label/value list, then tables, then a summary -- but the number of tables is not: the Production
/// Journal prints three (Raw Materials, Byproduct, Production Expenses), the Cash Transfer two
/// (Transferred From, Transferred To), the Payment two (Payment Details, Payment For). A DTO with
/// one <c>Lines</c> collection cannot say that, and the alternative -- a third and fourth bespoke
/// layout -- would have meant a fifth and sixth for Phase 28's documents.</para>
///
/// <para>So the document is <see cref="Sections"/>: an ordered list of titled tables, each
/// carrying its own columns. <c>DocumentPdfRenderer</c> renders that generically and knows nothing
/// about any <see cref="DocumentType"/>, which is what lets one layout serve all fifteen.</para>
///
/// <para><b>Values arrive pre-formatted as strings</b>, deliberately. This is a print DTO whose
/// only consumer is a PDF renderer, so presentation is its whole purpose -- and it is what lets the
/// handler render every business date through <c>RequestCalendar</c>, closing phase-23 Decision A's
/// "server output stays AD" limitation in one place rather than in the renderer's every call
/// site.</para>
/// </summary>
public sealed record PrintableDocumentDto(
    DocumentType DocumentType,
    string Title,
    string Code,
    string DateText,
    string? Reference,
    string OrganizationName,
    string? OrganizationAddress,
    string? OrganizationPhone,
    string? OrganizationEmail,
    string? OrganizationPan,
    string? OrganizationWebsite,
    string? PartyHeading,
    string? PartyLabel,
    string? PartyAddress,
    string PrintingTemplateName,
    IReadOnlyList<PrintableFieldDto> HeaderFields,
    IReadOnlyList<PrintableSectionDto> Sections,
    IReadOnlyList<PrintableFieldDto> Summary,
    string? Notes,
    string? Terms,
    string? CalendarNote);

/// <summary>A label/value pair -- the header block under the title, and the summary block at the
/// foot. <paramref name="Emphasise"/> is the one bold line a summary usually ends on (Grand Total,
/// Total Transfer, Cost Per Unit).</summary>
public sealed record PrintableFieldDto(string Label, string Value, bool Emphasise = false);

/// <summary>One titled table. <paramref name="TotalRow"/> is rendered bold beneath the rows when
/// present, matching the reference product's own per-section totals.</summary>
public sealed record PrintableSectionDto(
    string Title,
    IReadOnlyList<PrintableColumnDto> Columns,
    IReadOnlyList<PrintableRowDto> Rows,
    PrintableRowDto? TotalRow = null);

/// <summary><paramref name="Width"/> is a QuestPDF relative width, not points.</summary>
public sealed record PrintableColumnDto(string Header, float Width, bool AlignRight = false);

/// <summary>Cells are positional and must match the section's column count.</summary>
public sealed record PrintableRowDto(IReadOnlyList<string> Cells);
