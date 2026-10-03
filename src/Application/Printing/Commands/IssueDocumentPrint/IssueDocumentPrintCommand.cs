using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Printing.Commands.IssueDocumentPrint;

/// <summary>
/// Phase 67 -- prints an invoice or a credit note as a <b>counted</b> copy: writes the next
/// <see cref="InvoicePrint"/> / <see cref="CreditNotePrint"/> row, then renders the PDF that row
/// describes. The first copy is the original and every later one is marked "COPY OF ORIGINAL · printed
/// N times" (the Procedure Related to Computerized Invoicing, 2072, §6; phase-67-status.md Decision B).
///
/// <para><b>A command, so a POST.</b> The other fifteen printable types still print through the GET,
/// which writes nothing. For these two the GET refuses (Decision C): a GET that wrote would be a GET
/// that writes, and a GET that did not would hand out an uncounted, unmarked copy.</para>
///
/// <para><b>One count per bill</b> (Decision D). The rows are the till's (phases 62 and 63), under the
/// same unique index, so a till sale printed at the till and again from the invoice page is copy 2, and
/// two people printing at once cannot both be copy 2.</para>
///
/// <para><b>No new key</b> (Decision H): printing rides the document's own View key at its location,
/// exactly as the GET does, because a print is the document read onto paper.</para>
/// </summary>
public sealed record IssueDocumentPrintCommand(
    Guid OrganizationId, DocumentType DocumentType, Guid DocumentId, PrintMedium Medium)
    : IRequest<PrintableDocumentDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => PrintDocumentPermissions.ViewPermissionFor(DocumentType);

    public Guid LocationDocumentId => DocumentId;
}
