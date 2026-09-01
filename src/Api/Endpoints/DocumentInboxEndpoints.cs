using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Storage;
using ErpApp.Application.Tenancy.Commands.UpdateAiDocumentExtractionSetting;
using ErpApp.Application.Tenancy.Queries.GetAiDocumentExtractionSetting;
using ErpApp.Application.Workflow.Commands.ClearInboxDocumentExtraction;
using ErpApp.Application.Workflow.Commands.DeleteInboxDocument;
using ErpApp.Application.Workflow.Commands.ExtractInboxDocument;
using ErpApp.Application.Workflow.Commands.LinkInboxDocument;
using ErpApp.Application.Workflow.Commands.UpdateInboxDocument;
using ErpApp.Application.Workflow.Commands.UploadInboxDocument;
using ErpApp.Application.Workflow.Queries.GetInboxDocument;
using ErpApp.Application.Workflow.Queries.GetInboxDocumentForDownload;
using ErpApp.Application.Workflow.Queries.GetInboxDocumentPrefill;
using ErpApp.Application.Workflow.Queries.ListInboxDocuments;
using ErpApp.Domain.Common;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Api.Endpoints;

/// <summary>Phase 22 (FR-10.3) -- the Document inbox, under the Workflow context beside the
/// Transaction Approval queue.</summary>
public static class DocumentInboxEndpoints
{
    public static void MapDocumentInboxEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/workflow/inbox-documents")
            .WithTags("DocumentInbox")
            .RequireAuthorization();

        group.MapGet("/", async (
            Guid organizationId,
            UploadedDocumentStatus? status,
            DocumentType? linkedTransactionType,
            Guid? linkedTransactionId,
            string? search,
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListInboxDocumentsQuery(
                    organizationId, status, linkedTransactionType, linkedTransactionId, search,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        // A single IFormFile parameter makes this a multipart/form-data endpoint automatically, and
        // .NET auto-attaches antiforgery metadata to any form-binding endpoint. This app has no
        // app.UseAntiforgery() (its CSRF mitigation is the CORS allow-list plus the httpOnly JWT
        // cookie), so without DisableAntiforgery() every upload 500s with "contains anti-forgery
        // metadata, but a middleware was not found" -- exactly the Phase 18 bug, restated because
        // this is the second endpoint in the codebase to hit it.
        group.MapPost("/", async (
            Guid organizationId, IFormFile file, string? description, string? label,
            ISender sender, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            var result = await sender.Send(
                new UploadInboxDocumentCommand(
                    organizationId, file.FileName, file.Length, file.ContentType, stream, description, label),
                ct);
            return Results.Created(
                $"/api/organizations/{organizationId}/workflow/inbox-documents/{result.Id}", result);
        }).DisableAntiforgery();

        group.MapGet("/{id:guid}", async (Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetInboxDocumentQuery(organizationId, id), ct)));

        // Streams via IFileStorage directly (injected here, not routed through MediatR -- a raw
        // Stream is an awkward MediatR response shape), always behind the permission-checked,
        // org-scoped query first. Inline rather than as an attachment: this is the endpoint an
        // <img>/<iframe> points at for the side-by-side conversion pane and the source-document
        // panel on a transaction, and a Content-Disposition of attachment would make the browser
        // download it instead of rendering it.
        group.MapGet("/{id:guid}/content", async (
            Guid organizationId, Guid id, ISender sender, IFileStorage fileStorage, CancellationToken ct) =>
        {
            var metadata = await sender.Send(new GetInboxDocumentForDownloadQuery(organizationId, id), ct);
            var stream = await fileStorage.OpenReadAsync(metadata.StorageKey, ct);
            return Results.File(stream, metadata.ContentType);
        });

        group.MapGet("/{id:guid}/download", async (
            Guid organizationId, Guid id, ISender sender, IFileStorage fileStorage, CancellationToken ct) =>
        {
            var metadata = await sender.Send(new GetInboxDocumentForDownloadQuery(organizationId, id), ct);
            var stream = await fileStorage.OpenReadAsync(metadata.StorageKey, ct);
            return Results.File(stream, metadata.ContentType, metadata.FileName);
        });

        // The conversion's first half. Its permission key is the *target type's* own Create key, so
        // this 403s for a user who could never save the document it would pre-fill.
        group.MapGet("/{id:guid}/prefill/{targetType}", async (
            Guid organizationId, Guid id, DocumentType targetType, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetInboxDocumentPrefillQuery(organizationId, id, targetType), ct)));

        group.MapPut("/{id:guid}", async (
            Guid organizationId, Guid id, UpdateInboxDocumentRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new UpdateInboxDocumentCommand(organizationId, id, request.Description, request.Label, request.Status),
                ct)));

        // The conversion's second half: the target document already exists, created by its own
        // ordinary Create command with a human pressing Save.
        group.MapPost("/{id:guid}/link", async (
            Guid organizationId, Guid id, LinkInboxDocumentRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new LinkInboxDocumentCommand(organizationId, id, request.TransactionType, request.TransactionId), ct)));

        group.MapPost("/{id:guid}/extract", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ExtractInboxDocumentCommand(organizationId, id), ct)));

        group.MapDelete("/{id:guid}/extraction", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ClearInboxDocumentExtractionCommand(organizationId, id), ct)));

        group.MapDelete("/{id:guid}", async (Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteInboxDocumentCommand(organizationId, id), ct);
            return Results.NoContent();
        });

        // The tenant's own consent switch. Under the organization root rather than the inbox route,
        // because it is a tenant-wide setting the inbox merely happens to be the only reader of.
        var settings = app.MapGroup("/api/organizations/{organizationId:guid}/ai-document-extraction")
            .WithTags("DocumentInbox")
            .RequireAuthorization();

        settings.MapGet("/", async (Guid organizationId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetAiDocumentExtractionSettingQuery(organizationId), ct)));

        settings.MapPut("/", async (
            Guid organizationId, UpdateAiDocumentExtractionSettingRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new UpdateAiDocumentExtractionSettingCommand(organizationId, request.Enabled), ct)));
    }

    public sealed record UpdateInboxDocumentRequest(string? Description, string? Label, UploadedDocumentStatus Status);

    public sealed record LinkInboxDocumentRequest(DocumentType TransactionType, Guid TransactionId);

    public sealed record UpdateAiDocumentExtractionSettingRequest(bool Enabled);
}
