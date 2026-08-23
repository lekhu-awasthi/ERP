using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Storage;
using ErpApp.Application.Workflow.Commands.DeleteAttachment;
using ErpApp.Application.Workflow.Commands.UploadAttachment;
using ErpApp.Application.Workflow.Queries.GetAttachmentForDownload;
using ErpApp.Application.Workflow.Queries.ListAttachments;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class AttachmentsEndpoints
{
    public static void MapAttachmentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Attachments")
            .RequireAuthorization();

        group.MapGet("/contacts/{contactId:guid}/attachments", async (
            Guid organizationId, Guid contactId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListAttachmentsQuery(
                    organizationId, AttachmentParentType.Contact, contactId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize),
                ct);
            return Results.Ok(result);
        });

        // A single IFormFile parameter is enough for ASP.NET Core Minimal APIs to bind this as a
        // multipart/form-data endpoint automatically -- no [FromForm] attribute needed. .NET
        // auto-attaches antiforgery metadata to any form-binding endpoint by default; this app has
        // no app.UseAntiforgery() (its CSRF mitigation is the CORS allow-list in Program.cs, not
        // antiforgery tokens -- no other endpoint in this codebase uses them either), so that
        // metadata must be explicitly disabled per-endpoint or every upload 500s with
        // "contains anti-forgery metadata, but a middleware was not found."
        group.MapPost("/contacts/{contactId:guid}/attachments", async (
            Guid organizationId, Guid contactId, IFormFile file, ISender sender, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            var result = await sender.Send(
                new UploadAttachmentCommand(
                    organizationId, AttachmentParentType.Contact, contactId, file.FileName, file.Length,
                    file.ContentType, stream),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/attachments/{result.Id}", result);
        }).DisableAntiforgery();

        // Streams via IFileStorage directly (injected here, not routed through MediatR -- see
        // GetAttachmentForDownloadQuery's own doc comment) -- always behind this permission-checked,
        // org-scoped query, never a raw static path.
        group.MapGet("/attachments/{id:guid}/download", async (
            Guid organizationId, Guid id, ISender sender, IFileStorage fileStorage, CancellationToken ct) =>
        {
            var metadata = await sender.Send(new GetAttachmentForDownloadQuery(organizationId, id), ct);
            var stream = await fileStorage.OpenReadAsync(metadata.StorageKey, ct);
            return Results.File(stream, metadata.ContentType, metadata.FileName);
        });

        group.MapDelete("/attachments/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteAttachmentCommand(organizationId, id), ct);
            return Results.NoContent();
        });
    }
}
