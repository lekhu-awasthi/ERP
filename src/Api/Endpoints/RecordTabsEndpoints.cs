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
/// Phase 43 (39 carried item #1) -- the Documents and Activity tabs on the two <b>record</b> detail
/// pages, Deal and Task.
///
/// <para><b>Why a third route family rather than a widened <c>{documentType}</c> segment.</b>
/// <see cref="DocumentTabsEndpoints"/>'s group binds its segment to <see cref="DocumentType"/> and
/// resolves the parent with <c>DocumentParentTypes.For&lt;T&gt;()</c>, which answers only for the 15
/// transactional types. Deal and WorkTask are <see cref="DocumentType"/> members solely so the audit
/// feed can name them (see the enum), and routing them through that group would have meant relaxing
/// the one place whose narrowness is the reason an unroutable type is a 404 from routing rather than
/// something a validator has to catch. Contact already has its own family for the same reason; this
/// is the third, and the parent kind still comes from the route segment and never from a body.</para>
///
/// <para><b>Deal's Tasks tab needs nothing here.</b> <c>/tasks</c> has taken <c>parentType</c> and
/// <c>parentId</c> as ordinary parameters since phase 13, so appending <c>Deal</c> to
/// <c>TaskParentType</c> gave that tab a working API with no endpoint change -- exactly as phase 27a
/// found for the document Tasks tab. The Task detail page has no Tasks tab at all (a task does not
/// parent tasks), which is why only two of the three parent enums gained <c>WorkTask</c>.</para>
/// </summary>
public static class RecordTabsEndpoints
{
    public static void MapRecordTabsEndpoints(this IEndpointRouteBuilder app)
    {
        MapFamily(app, "deals", AttachmentParentType.Deal, CommentParentType.Deal, DocumentType.Deal);
        MapFamily(app, "tasks", AttachmentParentType.WorkTask, CommentParentType.WorkTask, DocumentType.WorkTask);
    }

    /// <summary>
    /// One family per record type, parameterised rather than copied. Two near-identical route blocks
    /// is precisely the shape phase 27a replaced with one shared map, and the reasoning is the same:
    /// agreement by construction, not by someone checking two call sites stay in sync.
    /// </summary>
    private static void MapFamily(
        IEndpointRouteBuilder app,
        string segment,
        AttachmentParentType attachmentParent,
        CommentParentType commentParent,
        DocumentType auditType)
    {
        var group = app
            .MapGroup($"/api/organizations/{{organizationId:guid}}/{segment}/{{recordId:guid}}")
            .WithTags("Record tabs")
            .RequireAuthorization();

        // --- Documents tab -------------------------------------------------------------------
        group.MapGet("/attachments", async (
            Guid organizationId, Guid recordId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListAttachmentsQuery(
                    organizationId, attachmentParent, recordId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize),
                ct);
            return Results.Ok(result);
        });

        // .DisableAntiforgery() for the reason every IFormFile endpoint in this codebase needs it: a
        // single IFormFile parameter makes this a form-binding endpoint, which .NET auto-decorates
        // with antiforgery metadata, which 500s without an app.UseAntiforgery() this app does not
        // have (phase-18 bug #1).
        group.MapPost("/attachments", async (
            Guid organizationId, Guid recordId, IFormFile file, ISender sender, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            var result = await sender.Send(
                new UploadAttachmentCommand(
                    organizationId, attachmentParent, recordId,
                    file.FileName, file.Length, file.ContentType, stream),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/attachments/{result.Id}", result);
        }).DisableAntiforgery();

        // Download and delete stay on the id-addressed routes in AttachmentsEndpoints, whose handlers
        // resolve the real permission from the row's own parent (phase-27a's AttachmentAccess
        // pattern) -- so they answer for these two parents with no change at all.

        // --- Activity tab --------------------------------------------------------------------
        group.MapGet("/comments", async (
            Guid organizationId, Guid recordId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListCommentsQuery(
                    organizationId, commentParent, recordId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/comments", async (
            Guid organizationId, Guid recordId, AddRecordCommentRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new AddCommentCommand(organizationId, commentParent, recordId, request.Content), ct);
            return Results.Created(
                $"/api/organizations/{organizationId}/{segment}/{recordId}/comments/{result.Id}", result);
        });

        group.MapGet("/activities", async (
            Guid organizationId, Guid recordId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListActivitiesQuery(
                    organizationId, auditType, recordId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize),
                ct);
            return Results.Ok(result);
        });
    }

    private sealed record AddRecordCommentRequest(string Content);
}
