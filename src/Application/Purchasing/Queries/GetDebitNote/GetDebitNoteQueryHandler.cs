using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.GetDebitNote;

public sealed class GetDebitNoteQueryHandler(IAppDbContext db) : IRequestHandler<GetDebitNoteQuery, DebitNoteDetailDto>
{
    public async Task<DebitNoteDetailDto> Handle(GetDebitNoteQuery request, CancellationToken cancellationToken)
    {
        var debitNote = await db.DebitNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Debit note not found.");

        IReadOnlyList<PostedGlLineDto>? glLines = null;

        if (debitNote.Status == DebitNoteStatus.Approved)
        {
            var glEntry = await db.GlJournalEntries
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x => x.SourceDocumentType == DocumentType.DebitNote && x.SourceDocumentId == debitNote.Id, cancellationToken);

            glLines = glEntry?.Lines.Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
        }

        return new DebitNoteDetailDto(
            debitNote.Id,
            debitNote.OrganizationId,
            debitNote.ContactId,
            debitNote.Code,
            debitNote.Date,
            debitNote.Reference,
            debitNote.Status,
            debitNote.ApprovedByUserId,
            debitNote.ApprovedAt,
            debitNote.CreatedAt,
            debitNote.ReferrerType,
            debitNote.ReferrerId,
            debitNote.Lines.Select(x => new DebitNoteLineDto(x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.Amount, x.VatAmount)).ToList(),
            glLines);
    }
}
