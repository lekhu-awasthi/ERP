using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Printing.Queries.PrintDocument;

/// <summary>
/// Phase 20d's print pipeline (FR-11.2, closes Phase 16c's deferred print-formatted output).
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
/// Only the 7 document types confirmed live to have a "Print"/"View Print Preview" action
/// wired (docs/phase-20d-status.md's scope decision) -- PrintDocumentPermissions.ViewPermissionFor
/// throws ArgumentOutOfRangeException for the rest, the same "define broadly, wire narrowly"
/// precedent CustomStatusPermissions.EditPermissionFor set in Phase 20b.
/// </summary>
public sealed record PrintDocumentQuery(Guid OrganizationId, DocumentType DocumentType, Guid DocumentId)
    : IRequest<PrintableDocumentDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PrintDocumentPermissions.ViewPermissionFor(DocumentType);
}

/// <summary>See SetCustomStatusCommand's CustomStatusPermissions for the identical precedent this
/// mirrors: a small switch throwing for anything not yet wired, rather than silently 403ing or
/// crashing with a generic error.</summary>
public static class PrintDocumentPermissions
{
    public static string ViewPermissionFor(DocumentType documentType) => documentType switch
    {
        DocumentType.Invoice => PermissionKeys.InvoiceView,
        DocumentType.Quotation => PermissionKeys.QuotationView,
        DocumentType.SalesOrder => PermissionKeys.SalesOrderView,
        DocumentType.PurchaseOrder => PermissionKeys.PurchaseOrderView,
        DocumentType.PurchaseBill => PermissionKeys.PurchaseBillView,
        DocumentType.JournalVoucher => PermissionKeys.JournalVoucherView,
        _ => throw new ArgumentOutOfRangeException(
            nameof(documentType), documentType, "Printing is not wired up for this document type yet."),
    };
}

/// <summary>One of two shapes (Lines XOR GlLines is populated) -- see DocumentPdfRenderer, which
/// picks its layout by which one is non-null rather than by DocumentType, so a future document
/// type just needs a DTO-building case here, not a new layout.</summary>
public sealed record PrintableDocumentDto(
    DocumentType DocumentType,
    string Code,
    DateOnly Date,
    string? Reference,
    string OrganizationName,
    string? OrganizationAddress,
    string? OrganizationPhone,
    string? OrganizationEmail,
    string? OrganizationPan,
    string? OrganizationWebsite,
    string? PartyLabel,
    string? PartyAddress,
    string PrintingTemplateName,
    IReadOnlyList<PrintableLineDto>? Lines,
    IReadOnlyList<PrintableGlLineDto>? GlLines,
    decimal? GrandTotal,
    decimal? DiscountPct);

public sealed record PrintableLineDto(
    string ProductLabel, decimal Quantity, decimal Rate, decimal DiscountPct, decimal Amount, decimal VatAmount);

public sealed record PrintableGlLineDto(string AccountLabel, decimal Debit, decimal Credit);
