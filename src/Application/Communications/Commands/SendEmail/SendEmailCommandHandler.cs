using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using ErpApp.Domain.Communications;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Communications.Commands.SendEmail;

/// <summary>
/// Claims the send and returns. See <c>EmailSendLog</c> for the ledger's contract and
/// <c>EmailSendJobProcessor</c> for what happens next.
/// </summary>
public sealed class SendEmailCommandHandler(
    IAppDbContext db,
    IFileStorage fileStorage,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<SendEmailCommand, SendEmailResult>
{
    public async Task<SendEmailResult> Handle(SendEmailCommand request, CancellationToken cancellationToken)
    {
        // Do-exactly-once, first pass: the overwhelmingly common case is a straightforward
        // double-submit that the row already committed by the first one can answer without touching
        // anything. The unique index below is what makes it correct under a genuine race; this read
        // is what makes it cheap.
        var existing = await db.EmailSendLogs.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId && x.RequestId == request.RequestId,
            cancellationToken);

        if (existing is not null)
        {
            await DiscardUploadsAsync(request, cancellationToken);
            return new SendEmailResult(existing.Id, AlreadyQueued: true);
        }

        var context = await EmailComposition.ResolveContextAsync(
            db, request.OrganizationId, request.DocumentType, request.ParentId, cancellationToken);

        await EmailComposition.EnsureParentExistsAsync(
            db, request.OrganizationId, request.DocumentType, request.ParentId, cancellationToken);

        // The real gate. See PermissionKeys.EmailSend for the two-layer derivation, and note this
        // runs after the parent has been proven to exist, so a cross-tenant id stays a 404.
        await EmailComposition.EnsureMayEmailParentAsync(
            db, request.OrganizationId, currentUser.UserId, request.DocumentType, cancellationToken);

        if (request.TemplateId is not null)
        {
            var templateBelongs = await db.EmailTemplates.AnyAsync(
                x => x.Id == request.TemplateId.Value
                     && x.OrganizationId == request.OrganizationId
                     && x.Context == context,
                cancellationToken);

            if (!templateBelongs)
            {
                throw new NotFoundException("Email template not found for this document.");
            }
        }

        var log = EmailSendLog.Queue(
            request.OrganizationId,
            request.RequestId,
            EmailTemplateContextsParent(context),
            request.ParentId,
            context,
            request.TemplateId,
            string.Join(EmailSendLog.AddressSeparator, request.To),
            string.Join(EmailSendLog.AddressSeparator, request.Cc),
            string.Join(EmailSendLog.AddressSeparator, request.Bcc),
            request.ReplyTo,
            request.Subject,
            request.Body,
            request.AttachDocumentPdf && request.DocumentType is not null,
            currentUser.UserId,
            timeProvider.GetUtcNow());

        db.EmailSendLogs.Add(log);

        foreach (var attachment in request.Attachments)
        {
            // Reported by the aggregate and added through the child DbSet -- appending to a tracked
            // parent's encapsulated collection is detected as Modified rather than Added
            // (phase-24 bug #1).
            db.EmailSendAttachments.Add(log.AddAttachment(
                attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.StorageKey));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Do-exactly-once, second pass: two concurrent submits of one RequestId, and this one
            // lost the unique index on (OrganizationId, RequestId). The winner's row is the answer.
            db.EmailSendLogs.Entry(log).State = EntityState.Detached;

            var winner = await db.EmailSendLogs.SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId && x.RequestId == request.RequestId,
                cancellationToken);

            if (winner is null)
            {
                throw;
            }

            await DiscardUploadsAsync(request, cancellationToken);
            return new SendEmailResult(winner.Id, AlreadyQueued: true);
        }

        return new SendEmailResult(log.Id, AlreadyQueued: false);
    }

    private static EmailParentType EmailTemplateContextsParent(Domain.Configuration.EmailTemplateContext context) =>
        Domain.Configuration.EmailTemplateContexts.ParentTypeFor(context);

    /// <summary>
    /// A duplicate submit uploaded its files again before reaching the handler, and those blobs now
    /// belong to nothing. Deleting them is the whole reason this method exists: leaving them would
    /// mean every double-click permanently leaked an attachment's worth of storage that no row
    /// references and no retention sweep can find.
    ///
    /// <para>A failure to delete is swallowed. The send is already accepted and the user is owed a
    /// confirmation; an orphaned blob is a cost, not a correctness failure — the same trade
    /// <c>ExportJobProcessor</c> accepts for its own orphan case.</para>
    /// </summary>
    private async Task DiscardUploadsAsync(SendEmailCommand request, CancellationToken cancellationToken)
    {
        foreach (var attachment in request.Attachments)
        {
            try
            {
                await fileStorage.DeleteAsync(attachment.StorageKey, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Intentionally ignored -- see the remarks.
            }
        }
    }
}
