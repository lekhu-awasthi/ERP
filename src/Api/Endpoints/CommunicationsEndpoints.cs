using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Storage;
using ErpApp.Application.Communications.Commands.CreateEmailTemplate;
using ErpApp.Application.Communications.Commands.SendEmail;
using ErpApp.Application.Communications.Commands.SetDefaultEmailTemplate;
using ErpApp.Application.Communications.Commands.UpdateEmailTemplate;
using ErpApp.Application.Communications.Queries.ListEmailLogs;
using ErpApp.Application.Communications.Queries.ListEmailTemplates;
using ErpApp.Application.Communications.Queries.PrepareEmail;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Api.Endpoints;

/// <summary>
/// Phase 30 (FR-11.1, FR-4.5) — the Send Email dialog, the email log, and Email templates.
///
/// <para><c>documentType</c> is a nullable query parameter throughout rather than a route segment,
/// because <c>null</c> is a real and common value: it means "this send is about a Contact, not a
/// document", which is the Contact detail page's own Send Email action.</para>
/// </summary>
public static class CommunicationsEndpoints
{
    public static void MapCommunicationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Communications")
            .RequireAuthorization();

        group.MapGet("/emails/prepare", async (
            Guid organizationId, DocumentType? documentType, Guid parentId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new PrepareEmailQuery(organizationId, documentType, parentId), ct);
            return Results.Ok(result);
        });

        // multipart/form-data, because the live dialog's drop zone accepts arbitrary files alongside
        // the message. A form-binding Minimal API endpoint gets antiforgery metadata automatically
        // and 500s without .DisableAntiforgery() -- this app's CSRF mitigation is the CORS
        // allow-list in Program.cs, exactly as AttachmentsEndpoints documents.
        group.MapPost("/emails", async (
            Guid organizationId,
            HttpRequest request,
            ISender sender,
            IFileStorage fileStorage,
            CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);

            // Files are streamed to storage here rather than carried through MediatR, because the
            // send is a background job: the request's streams are gone long before the runner needs
            // the bytes. EmailSendAttachment owns their deletion story -- see its remarks.
            var attachments = new List<SendEmailAttachmentInput>();
            foreach (var file in form.Files)
            {
                await using var stream = file.OpenReadStream();
                var key = await fileStorage.SaveAsync(stream, file.FileName, ct);
                attachments.Add(new SendEmailAttachmentInput(
                    file.FileName, file.ContentType, file.Length, key));
            }

            var command = new SendEmailCommand(
                organizationId,
                ParseGuid(form["requestId"], nameof(SendEmailCommand.RequestId)),
                ParseNullableEnum<DocumentType>(form["documentType"]),
                ParseGuid(form["parentId"], nameof(SendEmailCommand.ParentId)),
                ParseNullableGuid(form["templateId"]),
                ParseAddressList(form["to"]),
                ParseAddressList(form["cc"]),
                ParseAddressList(form["bcc"]),
                NullIfBlank(form["replyTo"]),
                form["subject"].ToString(),
                form["body"].ToString(),
                ParseBool(form["attachDocumentPdf"]),
                attachments);

            var result = await sender.Send(command, ct);
            return Results.Accepted($"/api/organizations/{organizationId}/emails/{result.EmailSendLogId}", result);
        }).DisableAntiforgery();

        group.MapGet("/emails", async (
            Guid organizationId, DocumentType? documentType, Guid parentId,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListEmailLogsQuery(
                    organizationId, documentType, parentId, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/email-templates", async (
            Guid organizationId, EmailTemplateContext? context, bool? includeInactive,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListEmailTemplatesQuery(organizationId, context, includeInactive ?? false), ct);
            return Results.Ok(result);
        });

        group.MapPost("/email-templates", async (
            Guid organizationId, CreateEmailTemplateRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateEmailTemplateCommand(
                    organizationId, body.Name, body.Context, body.Subject, body.Body,
                    body.ReplyTo, body.Cc, body.Bcc),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/email-templates/{result.Id}", result);
        });

        group.MapPut("/email-templates/{id:guid}", async (
            Guid organizationId, Guid id, UpdateEmailTemplateRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateEmailTemplateCommand(
                    organizationId, id, body.Name, body.Subject, body.Body,
                    body.ReplyTo, body.Cc, body.Bcc, body.IsActive),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/email-templates/{id:guid}/set-default", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new SetDefaultEmailTemplateCommand(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// A multipart field carrying addresses. Accepts either one comma/semicolon-separated field or
    /// repeated fields of the same name, because both are natural for a client to send and neither
    /// is worth a round trip to discover.
    /// </summary>
    private static IReadOnlyList<string> ParseAddressList(Microsoft.Extensions.Primitives.StringValues values) =>
        values
            .SelectMany(v => (v ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? NullIfBlank(Microsoft.Extensions.Primitives.StringValues value)
    {
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool ParseBool(Microsoft.Extensions.Primitives.StringValues value) =>
        bool.TryParse(value.ToString(), out var parsed) && parsed;

    private static Guid ParseGuid(Microsoft.Extensions.Primitives.StringValues value, string field) =>
        Guid.TryParse(value.ToString(), out var parsed)
            ? parsed
            : throw new BadHttpRequestException($"'{field}' is required and must be a GUID.");

    private static Guid? ParseNullableGuid(Microsoft.Extensions.Primitives.StringValues value) =>
        Guid.TryParse(value.ToString(), out var parsed) ? parsed : null;

    private static TEnum? ParseNullableEnum<TEnum>(Microsoft.Extensions.Primitives.StringValues value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value.ToString(), ignoreCase: true, out var parsed) ? parsed : null;
}

/// <summary>Phase 27b's lesson, applied on the way in: a command's parameter reaches nothing until
/// the Api's own request record carries it too. Every field here has a counterpart on the
/// command.</summary>
public sealed record CreateEmailTemplateRequest(
    string Name,
    EmailTemplateContext Context,
    string Subject,
    string Body,
    string? ReplyTo,
    string? Cc,
    string? Bcc);

public sealed record UpdateEmailTemplateRequest(
    string Name,
    string Subject,
    string Body,
    string? ReplyTo,
    string? Cc,
    string? Bcc,
    bool IsActive);
