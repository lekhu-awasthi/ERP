using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Sales.Posting;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.ApproveCreditNote;

public sealed class ApproveCreditNoteCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<CreditNotePostingInput> postingRule)
    : IRequestHandler<ApproveCreditNoteCommand, ApproveCreditNoteResult>
{
    public async Task<ApproveCreditNoteResult> Handle(ApproveCreditNoteCommand request, CancellationToken cancellationToken)
    {
        var creditNote = await db.CreditNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Credit note not found.");

        if (creditNote.Status != CreditNoteStatus.Draft)
        {
            throw new ConflictException("Only a Draft credit note can be approved.");
        }

        if (creditNote.Lines.Count == 0)
        {
            throw new ConflictException("A credit note needs at least one line to be approved.");
        }

        var postingInput = await CreditNoteAccountResolver.ResolveAsync(
            db, request.OrganizationId, creditNote.Lines.Select(x => (x.ProductId, x.Amount, x.VatAmount)), cancellationToken);

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.CreditNote, cancellationToken);

        creditNote.Approve(currentUser.UserId, code);

        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.CreditNote, creditNote.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveCreditNoteResult(creditNote.Id, creditNote.Code, creditNote.Status, creditNote.ApprovedAt);
    }
}
