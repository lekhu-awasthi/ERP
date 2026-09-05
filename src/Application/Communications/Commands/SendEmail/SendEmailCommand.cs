using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Communications.Commands.SendEmail;

/// <summary>
/// Queues one outbound email. Returns as soon as the ledger row is committed — the SMTP
/// conversation happens in <c>EmailSendJobProcessor</c> (Decision D).
///
/// <para><b><paramref name="RequestId"/> is the idempotency key</b>, minted by the client when it
/// opens the dialog. Submitting the same one twice yields one row and one email; opening the dialog
/// again mints a new one, and that resend is a new row, which is the semantic the roadmap fixes.
/// See <c>EmailSendLog</c> for the full argument and for why an occurrence key like the alert
/// scheduler's would be the wrong mechanism here.</para>
///
/// <para><b>Subject and Body arrive already resolved and possibly edited.</b> The server does not
/// re-substitute them: what the composer saw in the preview is what goes out, byte for byte. Any
/// other choice would let the text change between the preview and the send.</para>
/// </summary>
/// <param name="DocumentType">Null for a Contact-scoped send; see <c>PrepareEmailQuery</c>.</param>
/// <param name="TemplateId">Attribution only — the text is already resolved. Validated to belong to
/// this tenant and this context so the log cannot claim a template that never applied.</param>
/// <param name="AttachDocumentPdf">Rendered at send time from the same print pipeline the Print
/// action uses, so an emailed copy and a printed one cannot differ.</param>
/// <param name="Attachments">Extra files, already saved to storage by the endpoint. Empty is
/// normal.</param>
public sealed record SendEmailCommand(
    Guid OrganizationId,
    Guid RequestId,
    DocumentType? DocumentType,
    Guid ParentId,
    Guid? TemplateId,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string? ReplyTo,
    string Subject,
    string Body,
    bool AttachDocumentPdf,
    IReadOnlyList<SendEmailAttachmentInput> Attachments)
    : IRequest<SendEmailResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.EmailSend;
}

/// <param name="StorageKey">Opaque <c>IFileStorage</c> key the endpoint already wrote.</param>
public sealed record SendEmailAttachmentInput(
    string FileName, string ContentType, long SizeBytes, string StorageKey);

/// <param name="AlreadyQueued">True when this RequestId had already been accepted and this call
/// changed nothing — the do-exactly-once path. The client shows the same confirmation either
/// way.</param>
public sealed record SendEmailResult(Guid EmailSendLogId, bool AlreadyQueued);
