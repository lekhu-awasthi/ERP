using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Contacts.Commands.AddComment;
using ErpApp.Application.Contacts.Queries.ListActivities;
using ErpApp.Application.Contacts.Queries.ListComments;
using ErpApp.Application.Workflow.Commands.UploadAttachment;
using ErpApp.Application.Workflow.Queries.ListAttachments;
using ErpApp.Domain.Common;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Api.Endpoints;

/// <summary>
/// Phase 27a -- the document-side half of the Tasks / Documents / Activity tabs, live-confirmed as
/// the same three tabs (alongside Overview) on every transactional detail page.
///
/// <para><b>Tasks are absent here on purpose.</b> <c>/tasks</c> has taken <c>parentType</c> and
/// <c>parentId</c> as ordinary parameters since Phase 13, so widening <c>TaskParentType</c> gave the
/// document Tasks tab a working API with no endpoint change at all. Only attachments and comments
/// had Contact baked into their routes.</para>
///
/// <para>The parent kind comes from the route segment, never from a request body -- the same choice
/// the Contact routes and the Ageing/Statement routes make, and it means an unroutable
/// <c>DocumentType</c> is a 404 from routing rather than something a validator has to catch. The
/// <c>{documentType}</c> segment binds the enum by name, so the URL reads
/// <c>/documents/Invoice/{id}/attachments</c>.</para>
/// </summary>
public static class DocumentTabsEndpoints
{
    public static void MapDocumentTabsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/documents/{documentType}/{documentId:guid}")
            .WithTags("Document tabs")
            .RequireAuthorization();

        // --- Documents tab -------------------------------------------------------------------
        group.MapGet("/attachments", async (
            Guid organizationId, DocumentType documentType, Guid documentId, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListAttachmentsQuery(
                    organizationId,
                    DocumentParentTypes.For<AttachmentParentType>(documentType),
                    documentId,
                    page ?? 1,
                    pageSize ?? PagingDefaults.MaxPageSize),
                ct);
            return Results.Ok(result);
        });

        // .DisableAntiforgery() for the same reason the Contact upload needs it: a single IFormFile
        // parameter makes this a form-binding endpoint, which .NET auto-decorates with antiforgery
        // metadata, which 500s without an app.UseAntiforgery() this app deliberately does not have.
        group.MapPost("/attachments", async (
            Guid organizationId, DocumentType documentType, Guid documentId, IFormFile file,
            ISender sender, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            var result = await sender.Send(
                new UploadAttachmentCommand(
                    organizationId,
                    DocumentParentTypes.For<AttachmentParentType>(documentType),
                    documentId,
                    file.FileName,
                    file.Length,
                    file.ContentType,
                    stream),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/attachments/{result.Id}", result);
        }).DisableAntiforgery();

        // Download and delete stay on the existing id-addressed routes in AttachmentsEndpoints --
        // an attachment id is globally unique and those handlers now resolve the real permission
        // from the row's own parent, so there is nothing a document-scoped duplicate would add.

        // --- Activity tab --------------------------------------------------------------------
        group.MapGet("/comments", async (
            Guid organizationId, DocumentType documentType, Guid documentId, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListCommentsQuery(
                    organizationId,
                    DocumentParentTypes.For<CommentParentType>(documentType),
                    documentId,
                    page ?? 1,
                    pageSize ?? PagingDefaults.MaxPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/comments", async (
            Guid organizationId, DocumentType documentType, Guid documentId, AddDocumentCommentRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new AddCommentCommand(
                    organizationId,
                    DocumentParentTypes.For<CommentParentType>(documentType),
                    documentId,
                    request.Content),
                ct);
            return Results.Created(
                $"/api/organizations/{organizationId}/documents/{documentType}/{documentId}/comments/{result.Id}",
                result);
        });

        group.MapGet("/activities", async (
            Guid organizationId, DocumentType documentType, Guid documentId, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListActivitiesQuery(
                    organizationId, documentType, documentId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize),
                ct);
            return Results.Ok(result);
        });
    }

    private sealed record AddDocumentCommentRequest(string Content);
}
