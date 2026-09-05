using ErpApp.Application.Common.Email;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using ErpApp.Domain.Common;
using ErpApp.Domain.Communications;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErpApp.Application.Communications;

/// <summary>
/// Sends one queued email, start to finish, then returns.
///
/// <para><b>Claim-then-act, and at-most-once.</b> The row was already committed
/// <see cref="EmailSendStatus.Queued"/> by the HTTP request; this flips it to
/// <see cref="EmailSendStatus.Sending"/> and <b>commits that before touching SMTP</b>. Two
/// consequences, both deliberate and both the same ones phase 20e reasoned through for the alert
/// scheduler. Two runners cannot both send, because the second one's write loses the row's
/// concurrency token. And a process that dies between the commit and the SMTP response leaves a row stuck in
/// Sending that is <i>never</i> retried — a visible stall rather than a customer receiving the same
/// invoice twice. That trade is easier here than it was there: an alert is a daily summary, this is
/// a named person's bill.</para>
///
/// <para><b>It assumes the sender's identity via <c>IJobActingUser</c>, and that is not optional.</b>
/// The obvious reading — "this job only reads, so phase 20e's no-identity default applies, as it did
/// for phase 21b's exporter" — is wrong here, and the reason is worth stating because it is easy to
/// get backwards. The exporter reads through <i>org-filtered queries it owns</i>. This job renders
/// the attached PDF through <c>PrintDocumentQuery</c>, a permission-gated MediatR request, precisely
/// so an emailed PDF cannot drift from a printed one — and a MediatR request with no acting user
/// fails <c>AuthorizationBehavior</c>. So the choice is between duplicating the print pipeline and
/// naming a user, and duplicating it is the worse answer by a distance.
///
/// <para>What makes naming one defensible is exactly what made it defensible for phase 21a's
/// importer: the id is read off the row this runner just claimed, never from anything
/// client-supplied, and the user it names was authenticated and permission-checked by a real HTTP
/// request at queue time. It also buys something genuinely useful — <c>AuthorizationBehavior</c>
/// re-checks at <i>render</i> time, so a sender who lost access to the invoice between pressing Send
/// and the runner picking it up gets a failed send rather than a mailed document they may no longer
/// read.</para></para>
///
/// <para><b>No heartbeat and no lease</b>, unlike <c>ExportJob</c>. Those exist so an abandoned job
/// can be re-claimed and re-run; re-running is exactly what must not happen here.</para>
/// </summary>
public sealed class EmailSendJobProcessor(
    IAppDbContext db,
    IEmailSender emailSender,
    IFileStorage fileStorage,
    IDocumentPdfRenderer pdfRenderer,
    IJobActingUser actingUser,
    TimeProvider timeProvider,
    ILogger<EmailSendJobProcessor> logger) : IEmailSendJobProcessor
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var log = await db.EmailSendLogs
            .Include(x => x.Attachments)
            .Where(x => x.Status == EmailSendStatus.Queued)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (log is null)
        {
            return false;
        }

        if (!await TryClaimAsync(log, cancellationToken))
        {
            // Another runner took it between the read and the write. Returning true keeps the drain
            // loop going -- there may well be more work, and this tick has done none of it.
            return true;
        }

        // Named before anything is rendered -- see the type-level remarks. Single-shot per scope,
        // and the runner creates a scope per job, so one send can never act as another's sender.
        actingUser.Assume(log.SentByUserId);

        try
        {
            var message = await BuildMessageAsync(log, cancellationToken);
            await emailSender.SendAsync(message, cancellationToken);
            log.MarkSent(timeProvider.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send email {EmailSendLogId}.", log.Id);
            log.MarkFailed(timeProvider.GetUtcNow(), ex.Message);
        }

        // The blobs existed only so this job could read them after the request that received them
        // had ended, and it just has. Deleted before the terminal status is committed, so a failure
        // here leaves a harmless orphaned file rather than a row promising bytes that are gone --
        // the same ordering phase 21b's Decision E fixed for export artifacts.
        await PurgeAttachmentBlobsAsync(log, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Takes the row by writing its status under a concurrency token, so the claim is decided by
    /// the database rather than by this process's read. Returns false when another runner won.
    ///
    /// <para>A rowversion rather than phase-21a's "no token" rule, because that rule exists to
    /// protect a row with two writers and this one has a single writer after creation — see
    /// <c>EmailSendLog.RowVersion</c>. The loser detaches so the shared change tracker is not left
    /// holding a row that will fail every subsequent SaveChanges, exactly as
    /// <c>AlertDispatcher.TryClaimAsync</c> does.</para>
    /// </summary>
    private async Task<bool> TryClaimAsync(EmailSendLog log, CancellationToken cancellationToken)
    {
        log.MarkSending();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogInformation(
                ex, "Email {EmailSendLogId} was claimed by another runner; skipping.", log.Id);

            db.EmailSendLogs.Entry(log).State = EntityState.Detached;
            return false;
        }
    }

    private async Task<EmailMessage> BuildMessageAsync(EmailSendLog log, CancellationToken cancellationToken)
    {
        var attachments = new List<EmailAttachment>();

        if (log.AttachDocumentPdf)
        {
            var documentType = DocumentParentTypes.TryToDocumentType(log.ParentType)
                ?? throw new InvalidOperationException(
                    $"{log.ParentType} carries no document to attach; EmailSendLog.Queue should have rejected this.");

            var pdf = await pdfRenderer.RenderAsync(
                log.OrganizationId, documentType, log.ParentId, cancellationToken);

            attachments.Add(new EmailAttachment(pdf.FileName, RenderedDocumentPdf.ContentType, pdf.Content));
        }

        foreach (var stored in log.Attachments.Where(x => x.StorageKey is not null))
        {
            await using var stream = await fileStorage.OpenReadAsync(stored.StorageKey!, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);

            attachments.Add(new EmailAttachment(stored.FileName, stored.ContentType, buffer.ToArray()));
        }

        return new EmailMessage(
            log.To,
            log.Subject,
            log.Body,
            log.Cc,
            log.Bcc,
            log.ReplyTo,
            attachments,

            // The live composer is a rich-text editor, so a body is HTML. Phase 1a's plain-text
            // callers keep IsHtml false through EmailMessage.PlainText.
            IsHtml: true);
    }

    private async Task PurgeAttachmentBlobsAsync(EmailSendLog log, CancellationToken cancellationToken)
    {
        foreach (var attachment in log.Attachments.Where(x => x.StorageKey is not null))
        {
            try
            {
                await fileStorage.DeleteAsync(attachment.StorageKey!, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex, "Could not delete attachment blob for email {EmailSendLogId}; it is now orphaned.", log.Id);
            }
        }

        log.MarkAttachmentsPurged();
    }
}
