using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Commands.LinkInboxDocument;

/// <summary>
/// The second half of a conversion: the user has just pressed Save on the target document's own
/// form, the ordinary <c>CreateXCommand</c> has run through the whole pipeline and returned an id,
/// and this records which transaction came out of which scan (docs/phase-22-status.md, Decision B).
///
/// <para><b>This command creates nothing.</b> That separation is the entire point of choosing
/// prefill-and-submit over a server-side convert: numbering, validation, lock-date, feature gates,
/// GL posting rules and audit all stay in the target's own Create handler, unchanged and untouched
/// by this phase. A conversion that is abandoned before Save leaves the inbox exactly as it
/// was.</para>
///
/// <para>The target's existence is verified in this organization before the link is written, so a
/// forged transaction id cannot make a document point at another tenant's row -- there is no EF
/// global query filter in this codebase, so the check is explicit, per handler, as everywhere
/// else.</para>
/// </summary>
public sealed record LinkInboxDocumentCommand(
    Guid OrganizationId,
    Guid DocumentId,
    DocumentType TransactionType,
    Guid TransactionId)
    : IRequest<InboxDocumentDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InboxDocumentManage;
}

public sealed class LinkInboxDocumentCommandValidator : AbstractValidator<LinkInboxDocumentCommand>
{
    public LinkInboxDocumentCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.TransactionId).NotEmpty();
        RuleFor(x => x.TransactionType)
            .Must(InboxConversionTargets.IsSupported)
            .WithMessage("That document type cannot be created from the Document inbox.");
    }
}

public sealed class LinkInboxDocumentCommandHandler(IAppDbContext db, TimeProvider timeProvider)
    : IRequestHandler<LinkInboxDocumentCommand, InboxDocumentDto>
{
    public async Task<InboxDocumentDto> Handle(LinkInboxDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await db.UploadedDocuments.SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        if (!await TargetExistsAsync(request, cancellationToken))
        {
            throw new NotFoundException("The transaction to link was not found in this organization.");
        }

        if (document.IsLinked)
        {
            // A ConflictException, not a NotFound or a silent no-op: the user pressed "+ Add as" on
            // a row somebody else (or another tab) already converted, and the honest answer names
            // that rather than quietly producing a second transaction nothing would reconcile.
            throw new ConflictException(
                "This document has already been converted into a transaction. Upload the file again if you need a second one.");
        }

        document.LinkTransaction(request.TransactionType, request.TransactionId, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        var uploaderName = await db.Users
            .Where(x => x.Id == document.UploadedByUserId)
            .Select(x => x.FullName)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        return InboxDocumentMapper.ToDto(document, uploaderName);
    }

    /// <summary>
    /// Four concrete blocks rather than one generic helper over a selector -- EF Core cannot
    /// translate a captured Func inside Where (phase-9-status.md's bug #1, and Phase 12's
    /// 13-concrete-blocks precedent). Also the exact shape a fifth conversion target would extend.
    /// </summary>
    private Task<bool> TargetExistsAsync(LinkInboxDocumentCommand request, CancellationToken cancellationToken) =>
        request.TransactionType switch
        {
            DocumentType.Invoice => db.Invoices.AnyAsync(
                x => x.Id == request.TransactionId && x.OrganizationId == request.OrganizationId, cancellationToken),
            DocumentType.PurchaseBill => db.PurchaseBills.AnyAsync(
                x => x.Id == request.TransactionId && x.OrganizationId == request.OrganizationId, cancellationToken),
            DocumentType.Expense => db.Expenses.AnyAsync(
                x => x.Id == request.TransactionId && x.OrganizationId == request.OrganizationId, cancellationToken),
            DocumentType.Payment => db.Payments.AnyAsync(
                x => x.Id == request.TransactionId && x.OrganizationId == request.OrganizationId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request), request.TransactionType, "Not a supported Document inbox conversion target."),
        };
}
