using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Communications;
using Microsoft.EntityFrameworkCore;
using MediatR;

namespace ErpApp.Application.Communications.Queries.PrepareEmail;

public sealed class PrepareEmailQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<PrepareEmailQuery, PreparedEmailDto>
{
    public async Task<PreparedEmailDto> Handle(PrepareEmailQuery request, CancellationToken cancellationToken)
    {
        var composed = await EmailComposition.ComposeAsync(
            db, request.OrganizationId, currentUser.UserId, request.DocumentType, request.ParentId,
            templateId: null, cancellationToken);

        // The real gate, now that the parent is known and has been proven to exist. Deliberately
        // after the load, so an id from another organization stays a 404 rather than becoming a
        // probe that distinguishes "exists elsewhere" from "does not exist" -- the same ordering
        // DeleteAttachmentCommandHandler uses.
        await EmailComposition.EnsureMayEmailParentAsync(
            db, request.OrganizationId, currentUser.UserId, request.DocumentType, cancellationToken);

        var templates = await db.EmailTemplates
            .Where(x => x.OrganizationId == request.OrganizationId
                        && x.Context == composed.Context
                        && x.IsActive)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new EmailTemplateOptionDto(x.Id, x.Name, x.IsDefault))
            .ToListAsync(cancellationToken);

        var suggested = await EmailComposition.SuggestedRecipientsAsync(
            db, request.OrganizationId, composed.ContactId, cancellationToken);

        // Reply-To defaults to the signed-in user when the template does not name one -- live
        // behaviour on both the dialog and the template form (docs/phase-30-status.md, Step 1.4).
        var replyTo = composed.Template?.ReplyTo;
        if (string.IsNullOrWhiteSpace(replyTo))
        {
            replyTo = await db.Users
                .Where(x => x.Id == currentUser.UserId)
                .Select(x => x.Email)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return new PreparedEmailDto(
            composed.Context,
            EmailMergeFields.GroupNameFor(composed.Context),
            templates,
            composed.Template?.Id,
            composed.Subject,
            composed.Body,
            replyTo,
            EmailSendLog.ParseAddresses(composed.Template?.Cc),
            EmailSendLog.ParseAddresses(composed.Template?.Bcc),
            suggested,
            CanAttachDocumentPdf: request.DocumentType is not null,
            composed.DocumentCode,
            EmailMergeResolver.UnresolvedTokens(composed.Subject)
                .Concat(EmailMergeResolver.UnresolvedTokens(composed.Body))
                .Distinct(StringComparer.Ordinal)
                .ToList());
    }
}
