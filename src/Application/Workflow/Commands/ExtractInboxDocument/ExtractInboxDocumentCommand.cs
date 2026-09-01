using System.Text.Json;
using ErpApp.Application.Common.DocumentExtraction;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using ErpApp.Domain.Common;
using ErpApp.Domain.Workflow;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Commands.ExtractInboxDocument;

/// <summary>
/// Runs AI-assisted extraction against one uploaded scan (FR-10.3's stretch half) and stores the
/// result as a <i>suggestion</i> on the document. See docs/phase-22-status.md, Decision C, for the
/// product decision this implements; the parts that matter to a reader of this file:
///
/// <para><b>Explicit, never automatic.</b> Nothing runs this on upload. A person clicks Extract on a
/// row, having been told on that screen what the button does. Two independent gates must both be
/// open first: the tenant has opted in (<c>TenantSettings.AiDocumentExtractionEnabled</c>, default
/// <b>off</b>) and the acting user holds <c>Workflow.InboxDocument.Extract</c> (default Admin-only).
/// </para>
///
/// <para><b>Synchronous, with the vendor timeout inside the extractor.</b> No background job and no
/// new job table -- phase-21c's Decision C test asked the right two questions and both answer no
/// here: an extraction is not a spreadsheet of rows (every <c>ImportJob</c> row-count column would
/// be permanently null), and its loop is not a loop at all. A user is staring at the screen waiting
/// for one document; the status field on the aggregate is what a poller would have read anyway.</para>
///
/// <para><b>Failure is an outcome, not an error.</b> A timeout, a 429, a garbage response or a
/// missing credential all return 200 with the document's own status set, because the entire point is
/// that the document stays exactly as convertible by hand as it was a second earlier. The only
/// things that throw here are the gates -- and a caller asking to extract from a spreadsheet.</para>
/// </summary>
public sealed record ExtractInboxDocumentCommand(Guid OrganizationId, Guid DocumentId)
    : IRequest<InboxDocumentDto>, IRequirePermission, IOrganizationScoped, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.InboxDocumentExtract;

    // Audited: the one action in the product that sends a customer's business document outside it.
    // AuditBehavior's prefix list gained "Extract" for exactly this command.
    public DocumentType AuditDocumentType => DocumentType.DocumentExtraction;

    public Guid AuditDocumentId => DocumentId;
}

public sealed class ExtractInboxDocumentCommandValidator : AbstractValidator<ExtractInboxDocumentCommand>
{
    public ExtractInboxDocumentCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.DocumentId).NotEmpty();
    }
}

public sealed class ExtractInboxDocumentCommandHandler(
    IAppDbContext db,
    IFileStorage fileStorage,
    IDocumentExtractor extractor,
    TimeProvider timeProvider)
    : IRequestHandler<ExtractInboxDocumentCommand, InboxDocumentDto>
{
    public async Task<InboxDocumentDto> Handle(ExtractInboxDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await db.UploadedDocuments.SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        // The tenant's consent, re-read on every single run rather than cached: withdrawing consent
        // has to stop the next extraction, not the next process restart.
        var tenantOptedIn = await db.TenantSettings
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => x.AiDocumentExtractionEnabled)
            .SingleOrDefaultAsync(cancellationToken);

        if (!tenantOptedIn)
        {
            throw new ConflictException(
                "AI-assisted extraction is turned off for this organization. An Admin can turn it on from the Document inbox.");
        }

        if (!InboxDocumentValidation.IsExtractable(document.FileName))
        {
            throw new ConflictException(
                "Extraction only works on images and PDFs. This document can still be converted by hand.");
        }

        var outcome = await RunAsync(document.StorageKey, document.FileName, document.ContentType, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (outcome.Succeeded && outcome.Data is { } data)
        {
            document.RecordExtraction(
                DocumentExtractionStatus.Succeeded,
                JsonSerializer.Serialize(data, InboxDocumentMapper.SerializerOptions),
                outcome.ModelId,
                null,
                now);
        }
        else
        {
            document.RecordExtraction(
                outcome.Unavailable ? DocumentExtractionStatus.Unavailable : DocumentExtractionStatus.Failed,
                null,
                outcome.ModelId,
                outcome.FailureReason,
                now);
        }

        await db.SaveChangesAsync(cancellationToken);

        var uploaderName = await db.Users
            .Where(x => x.Id == document.UploadedByUserId)
            .Select(x => x.FullName)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        return InboxDocumentMapper.ToDto(document, uploaderName);
    }

    /// <summary>
    /// Belt-and-braces around <see cref="IDocumentExtractor"/>'s own contract that it never throws
    /// for a vendor problem. An implementation that breaks that contract must still not be able to
    /// turn "the AI is having a bad day" into a 500 on a page whose primary feature does not involve
    /// the AI at all. A cancelled *request* is re-thrown -- the user navigated away, and there is no
    /// outcome to record.
    /// </summary>
    private async Task<DocumentExtractionOutcome> RunAsync(
        string storageKey, string fileName, string contentType, CancellationToken cancellationToken)
    {
        try
        {
            await using var content = await fileStorage.OpenReadAsync(storageKey, cancellationToken);
            return await extractor.ExtractAsync(content, fileName, contentType, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DocumentExtractionOutcome.Failure(
                $"Extraction could not be completed ({ex.GetType().Name}). The document can still be converted by hand.");
        }
    }
}
