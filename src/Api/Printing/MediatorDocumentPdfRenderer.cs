using ErpApp.Application.Communications;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Api.Printing;

/// <summary>
/// The Api-side implementation of Application's <see cref="IDocumentPdfRenderer"/> — see that
/// interface for why the dependency inverts rather than moving QuestPDF down a layer.
///
/// <para>It runs <c>PrintDocumentQuery</c> through MediatR rather than reaching into the handler, so
/// an emailed PDF is byte-identical to a printed one <i>by construction</i>, including the file
/// name. Two consequences worth stating. Any future change to the print pipeline reaches emailed
/// copies for free. And the query's own permission check runs again here — which is correct but
/// subtle: this executes inside a background job with no <c>HttpContext</c>, so
/// <c>AuthorizationBehavior</c> has no acting user to check.</para>
///
/// <para><b>That re-check is why <c>EmailSendJobProcessor</c> assumes the sender's identity through
/// <c>IJobActingUser</c> before calling this.</b> Without one, every "Attach PDF" send would fail
/// authorization inside its own background job — a failure that would look like an SMTP problem and
/// be diagnosed as one. With one, the check is real: a sender who lost access to the document
/// between queueing the mail and the runner picking it up gets a recorded failure instead of an
/// attachment they may no longer read.</para>
/// </summary>
public sealed class MediatorDocumentPdfRenderer(ISender sender) : IDocumentPdfRenderer
{
    public async Task<RenderedDocumentPdf> RenderAsync(
        Guid organizationId,
        DocumentType documentType,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var dto = await sender.Send(new PrintDocumentQuery(organizationId, documentType, documentId), cancellationToken);

        // Same name PrintingEndpoints gives the download, so a recipient's attachment and the
        // sender's own printout are indistinguishable.
        return new RenderedDocumentPdf($"{documentType}_{dto.Code}.pdf", DocumentPdfRenderer.Render(dto));
    }
}
