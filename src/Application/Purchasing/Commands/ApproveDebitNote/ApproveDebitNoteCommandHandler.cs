using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.ApproveDebitNote;

public sealed class ApproveDebitNoteCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<DebitNotePostingInput> postingRule)
    : IRequestHandler<ApproveDebitNoteCommand, ApproveDebitNoteResult>
{
    public async Task<ApproveDebitNoteResult> Handle(ApproveDebitNoteCommand request, CancellationToken cancellationToken)
    {
        var debitNote = await db.DebitNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Debit note not found.");

        if (debitNote.Status != DebitNoteStatus.Draft)
        {
            throw new ConflictException("Only a Draft debit note can be approved.");
        }

        if (debitNote.Lines.Count == 0)
        {
            throw new ConflictException("A debit note needs at least one line to be approved.");
        }

        var postingInput = await DebitNoteAccountResolver.ResolveAsync(
            db, request.OrganizationId, debitNote.Lines.Select(x => (x.ProductId, x.Amount, x.VatAmount)), cancellationToken);

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.DebitNote, cancellationToken);

        debitNote.Approve(currentUser.UserId, code);

        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.DebitNote, debitNote.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveDebitNoteResult(debitNote.Id, debitNote.Code, debitNote.Status, debitNote.ApprovedAt);
    }
}
