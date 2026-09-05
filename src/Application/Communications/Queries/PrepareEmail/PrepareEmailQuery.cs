using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Communications.Queries.PrepareEmail;

/// <summary>
/// Everything the Send Email dialog needs to open: the templates it may offer, the one it defaults
/// to, and that template's subject and body <b>with merge fields already resolved</b>.
///
/// <para>That resolution is the whole point, and it is live-confirmed behaviour rather than a
/// convenience (docs/phase-30-status.md, Step 1.4): the reference dialog opens showing
/// "Hello Adhitya Bhandari, Your invoice 045 …", not the template's raw placeholders. So what the
/// user edits and sends is <b>the document's own text</b>, seeded from a template — never a
/// template reference resolved later, on a server, after the sender has stopped looking. Phase 27b
/// reached the same conclusion for Terms and Conditions; this is the second mechanism to land on
/// it, which makes it the pattern rather than a one-off.</para>
///
/// <para>Declares <see cref="PermissionKeys.EmailSend"/> rather than the parent's View key, and the
/// handler re-checks the parent's View key once the parent is known — see that constant for the
/// two-layer derivation. Preparing a draft reveals the document's totals and the contact's
/// addresses, so it is gated exactly as sending is; there is no cheaper read here.</para>
/// </summary>
/// <param name="DocumentType">The document being emailed about, or <b>null</b> for the Contact
/// detail page's own Send Email action — in which case <paramref name="ParentId"/> is a Contact id
/// and the context is <see cref="EmailTemplateContext.General"/>. A Payment's context also depends
/// on its direction, which the handler reads off the row rather than taking from the caller.</param>
/// <param name="ParentId">The document's id, or the Contact's.</param>
public sealed record PrepareEmailQuery(
    Guid OrganizationId,
    DocumentType? DocumentType,
    Guid ParentId)
    : IRequest<PreparedEmailDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.EmailSend;
}

/// <param name="Templates">Every active template for this context, default first.</param>
/// <param name="SuggestedTo">Addresses the live "More…" picker would offer — the contact's own
/// email, plus its personnel's. Empty is normal and not an error: live, this contact had none and
/// the picker read "No data found".</param>
/// <param name="UnresolvedTokens">Tokens still standing after substitution. Surfaced so the composer
/// sees a typo'd placeholder before a customer does — see <see cref="EmailMergeResolver"/> for why
/// they are left standing rather than blanked.</param>
public sealed record PreparedEmailDto(
    EmailTemplateContext Context,
    string ContextName,
    IReadOnlyList<EmailTemplateOptionDto> Templates,
    Guid? DefaultTemplateId,
    string Subject,
    string Body,
    string? ReplyTo,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    IReadOnlyList<string> SuggestedTo,
    bool CanAttachDocumentPdf,
    string? DocumentCode,
    IReadOnlyList<string> UnresolvedTokens);

public sealed record EmailTemplateOptionDto(Guid Id, string Name, bool IsDefault);
