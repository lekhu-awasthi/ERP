using ErpApp.Api.Printing;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Api.Endpoints;

/// <summary>Phase 20d -- one generic print endpoint for every wired document type (see
/// PrintDocumentPermissions for which ones), rather than a route per document type. The handler
/// returns a plain DTO; QuestPDF rendering happens here, at the Api layer, the same split
/// ReportSpreadsheetExporter uses for ClosedXML.</summary>
public static class PrintingEndpoints
{
    public static void MapPrintingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/organizations/{organizationId:guid}/print/{documentType}/{documentId:guid}", async (
            Guid organizationId, DocumentType documentType, Guid documentId, ISender sender, CancellationToken ct) =>
        {
            var dto = await sender.Send(new PrintDocumentQuery(organizationId, documentType, documentId), ct);
            var pdfBytes = DocumentPdfRenderer.Render(dto);
            return Results.File(pdfBytes, "application/pdf", $"{documentType}_{dto.Code}.pdf");
        })
        .WithTags("Printing")
        .RequireAuthorization();
    }
}
