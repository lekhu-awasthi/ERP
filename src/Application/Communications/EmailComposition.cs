using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;
using ErpApp.Domain.Communications;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Communications;

/// <summary>One resolved draft: the context, the contact, and the substituted text.</summary>
public sealed record ComposedEmail(
    EmailTemplateContext Context,
    EmailParentType ParentType,
    Guid? ContactId,
    string? DocumentCode,
    EmailTemplate? Template,
    string Subject,
    string Body);

/// <summary>
/// The shared middle of Phase 30: resolving <i>which</i> context a send is in, re-checking the
/// caller may see the parent, and substituting the template.
///
/// <para>It exists because <c>PrepareEmailQuery</c> and <c>SendEmailCommand</c> must agree
/// completely — the draft the dialog previews and the message the job sends have to come from one
/// derivation, or a user edits one thing and mails another. Phase 26b's shared-reader lesson, in
/// the place where a divergence would be most embarrassing.</para>
/// </summary>
public static class EmailComposition
{
    /// <summary>
    /// Resolves the context for a request, reading a Payment's direction off the row rather than
    /// trusting the caller. Also the place a non-emailable document type is rejected.
    /// </summary>
    public static async Task<EmailTemplateContext> ResolveContextAsync(
        IAppDbContext db, Guid organizationId, DocumentType? documentType, Guid parentId, CancellationToken ct)
    {
        if (documentType is null)
        {
            return EmailTemplateContext.General;
        }

        if (!DocumentMechanisms.Emailable.Contains(documentType.Value))
        {
            throw new ConflictException(
                $"{documentType} has no Send Email action. See DocumentMechanisms.Emailable.");
        }

        if (documentType.Value != DocumentType.Payment)
        {
            return EmailTemplateContexts.For(documentType.Value);
        }

        var direction = await db.Payments
            .Where(x => x.Id == parentId && x.OrganizationId == organizationId)
            .Select(x => (Domain.Payments.PaymentDirection?)x.Direction)
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Payment not found.");

        return EmailTemplateContexts.For(DocumentType.Payment, direction);
    }

    /// <summary>
    /// Proves the parent exists in this organization, so a send is never queued against a document
    /// the job would then fail to find.
    ///
    /// <para>Called <b>before</b> the permission re-check, deliberately and for
    /// <c>DeleteAttachmentCommandHandler</c>'s reason: an id belonging to another organization must
    /// stay a 404 rather than becoming a probe that distinguishes "exists elsewhere" from "does not
    /// exist". The blanket <see cref="PermissionKeys.EmailSend"/> key has already been checked by
    /// <c>AuthorizationBehavior</c> at this point, so an unauthenticated or non-member caller never
    /// reaches here at all.</para>
    /// </summary>
    public static async Task EnsureParentExistsAsync(
        IAppDbContext db, Guid organizationId, DocumentType? documentType, Guid parentId, CancellationToken ct)
    {
        var exists = documentType switch
        {
            null => await db.Contacts.AnyAsync(x => x.Id == parentId && x.OrganizationId == organizationId, ct),
            DocumentType.Quotation =>
                await db.Quotations.AnyAsync(x => x.Id == parentId && x.OrganizationId == organizationId, ct),
            DocumentType.SalesOrder =>
                await db.SalesOrders.AnyAsync(x => x.Id == parentId && x.OrganizationId == organizationId, ct),
            DocumentType.Invoice =>
                await db.Invoices.AnyAsync(x => x.Id == parentId && x.OrganizationId == organizationId, ct),
            DocumentType.CreditNote =>
                await db.CreditNotes.AnyAsync(x => x.Id == parentId && x.OrganizationId == organizationId, ct),
            DocumentType.PurchaseOrder =>
                await db.PurchaseOrders.AnyAsync(x => x.Id == parentId && x.OrganizationId == organizationId, ct),

            // A Payment's existence was already proven by ResolveContextAsync, which had to read the
            // row to learn its direction.
            DocumentType.Payment => true,

            _ => throw new ArgumentOutOfRangeException(
                nameof(documentType), documentType, "This document type has no Send Email action."),
        };

        if (!exists)
        {
            throw new NotFoundException(
                documentType is null ? "Contact not found." : $"{documentType} not found.");
        }
    }

    /// <summary>
    /// The second permission layer. <see cref="PermissionKeys.EmailSend"/> got the caller past
    /// <c>AuthorizationBehavior</c>'s org-membership check; this is the real gate — you may email a
    /// document exactly when you may view it. Throws the identical <c>ForbiddenException</c> shape
    /// as the behavior, so a caller cannot tell the two layers apart.
    ///
    /// <para>A Contact-scoped send rides <c>Contacts.Contact.View</c> for the same reason.</para>
    /// </summary>
    public static Task EnsureMayEmailParentAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid userId,
        DocumentType? documentType,
        CancellationToken ct)
    {
        var key = documentType is null
            ? PermissionKeys.ContactView
            : PrintDocumentPermissions.ViewPermissionFor(documentType.Value);

        return GrantedPermissionReader.EnsureGrantedAsync(db, organizationId, userId, key, ct);
    }

    /// <summary>
    /// Loads the parent, picks the template, and substitutes. <paramref name="templateId"/> null
    /// means "use the context's default".
    /// </summary>
    public static async Task<ComposedEmail> ComposeAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid actingUserId,
        DocumentType? documentType,
        Guid parentId,
        Guid? templateId,
        CancellationToken ct)
    {
        var context = await ResolveContextAsync(db, organizationId, documentType, parentId, ct);

        Guid? contactId;
        string? documentCode = null;
        EmailDocumentFacts? facts = null;

        if (documentType is null)
        {
            var exists = await db.Contacts.AnyAsync(
                x => x.Id == parentId && x.OrganizationId == organizationId, ct);

            if (!exists)
            {
                throw new NotFoundException("Contact not found.");
            }

            contactId = parentId;
        }
        else
        {
            facts = await EmailMergeValueReader.ReadDocumentAsync(
                db, organizationId, documentType.Value, parentId, ct);
            contactId = facts.ContactId;
            documentCode = facts.Code;
        }

        var values = await EmailMergeValueReader.ReadFixedAsync(db, organizationId, actingUserId, contactId, ct);

        if (facts is not null)
        {
            EmailMergeValueReader.AddDocumentValues(values, facts, values["CONTACT_NAME"]);
        }

        var template = await PickTemplateAsync(db, organizationId, context, templateId, ct);

        var subject = EmailMergeResolver.Apply(
            template?.Subject ?? DefaultSubjectFor(context, documentCode), values, context);
        var body = EmailMergeResolver.Apply(template?.Body ?? DefaultBody, values, context);

        return new ComposedEmail(
            context,
            EmailTemplateContexts.ParentTypeFor(context),
            contactId,
            documentCode,
            template,
            subject,
            body);
    }

    /// <summary>
    /// The named template if one was asked for, else the context's default, else its
    /// first-created active one, else null.
    /// </summary>
    /// <exception cref="NotFoundException">A template was named and is not this tenant's, is
    /// inactive, or belongs to another context — the last of which matters: an Invoice template
    /// applied to a Purchase Order would render half its tokens as raw placeholders.</exception>
    private static async Task<EmailTemplate?> PickTemplateAsync(
        IAppDbContext db, Guid organizationId, EmailTemplateContext context, Guid? templateId, CancellationToken ct)
    {
        if (templateId is not null)
        {
            return await db.EmailTemplates.SingleOrDefaultAsync(
                x => x.Id == templateId.Value
                     && x.OrganizationId == organizationId
                     && x.Context == context
                     && x.IsActive,
                ct)
                ?? throw new NotFoundException("Email template not found for this document.");
        }

        var candidates = await db.EmailTemplates
            .Where(x => x.OrganizationId == organizationId && x.Context == context && x.IsActive)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(ct);

        return candidates.FirstOrDefault();
    }

    /// <summary>A tenant with no template for this context still gets a usable draft rather than a
    /// blank dialog — the same courtesy phase 27b's balance-confirmation letter extends, and for the
    /// same reason: an empty screen looks broken, and the user is going to edit this text
    /// anyway.</summary>
    internal const string DefaultBody =
        "Hello $[CONTACT_NAME]$,\n\nPlease find the attached document from $[ORGANIZATION_NAME]$.\n\n"
        + "Kind regards,\n$[USER_NAME]$\n$[ORGANIZATION_NAME]$";

    internal static string DefaultSubjectFor(EmailTemplateContext context, string? documentCode) =>
        documentCode is null
            ? "$[ORGANIZATION_NAME]$"
            : $"{EmailMergeFields.GroupNameFor(context)} {documentCode} from $[ORGANIZATION_NAME]$";

    /// <summary>
    /// Addresses the live "More…" picker offers: the contact's own, then its personnel's, deduped
    /// case-insensitively and in that order. Empty is a normal outcome, not an error.
    /// </summary>
    public static async Task<IReadOnlyList<string>> SuggestedRecipientsAsync(
        IAppDbContext db, Guid organizationId, Guid? contactId, CancellationToken ct)
    {
        if (contactId is null)
        {
            return [];
        }

        var contactEmail = await db.Contacts
            .Where(x => x.Id == contactId.Value && x.OrganizationId == organizationId)
            .Select(x => x.Email)
            .SingleOrDefaultAsync(ct);

        var personnelEmails = await db.ContactPersonnel
            .Where(x => x.ContactId == contactId.Value && x.OrganizationId == organizationId && x.Email != null)
            .OrderBy(x => x.Name)
            .Select(x => x.Email!)
            .ToListAsync(ct);

        return new[] { contactEmail }
            .Concat(personnelEmails)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
