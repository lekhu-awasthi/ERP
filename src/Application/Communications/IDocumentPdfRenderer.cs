using ErpApp.Domain.Common;

namespace ErpApp.Application.Communications;

/// <summary>
/// Renders a document to PDF bytes. Phase 30 needs this seam because the "Attach Invoice PDF"
/// checkbox has to produce a file from a <b>background job</b>, and QuestPDF lives in the Api layer
/// (<c>ErpApp.Api.Printing.DocumentPdfRenderer</c>) precisely so that Application stays free of a
/// rendering-library dependency — the same split <c>ReportSpreadsheetExporter</c>'s callers use for
/// ClosedXML.
///
/// <para>Until now that was free: the print endpoint runs in the Api layer, so it could call the
/// renderer directly. A job cannot. So the dependency inverts — Application declares the need, the
/// Api implements it over the renderer it already owns (Api → Application is the allowed
/// direction), and <c>Program.cs</c> registers the adapter as the composition root. Nothing about
/// the layering rule bends; this is what the rule is for.</para>
///
/// <para>The implementation runs <c>PrintDocumentQuery</c> through MediatR, so an attached PDF is
/// byte-identical to the one the Print action produces, and any future change to the print pipeline
/// reaches emailed copies for free.</para>
/// </summary>
public interface IDocumentPdfRenderer
{
    /// <summary>Renders, and reports the file name the print endpoint would have used.</summary>
    Task<RenderedDocumentPdf> RenderAsync(
        Guid organizationId, DocumentType documentType, Guid documentId, CancellationToken cancellationToken = default);
}

public sealed record RenderedDocumentPdf(string FileName, byte[] Content)
{
    public const string ContentType = "application/pdf";
}
