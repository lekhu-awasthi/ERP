using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.GetCreditNote;

public sealed class GetCreditNoteQueryHandler(IAppDbContext db) : IRequestHandler<GetCreditNoteQuery, CreditNoteDetailDto>
{
    public async Task<CreditNoteDetailDto> Handle(GetCreditNoteQuery request, CancellationToken cancellationToken)
    {
        var creditNote = await db.CreditNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Credit note not found.");

        IReadOnlyList<PostedGlLineDto>? glLines = null;

        if (creditNote.Status == CreditNoteStatus.Approved)
        {
            var glEntry = await db.GlJournalEntries
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x => x.SourceDocumentType == DocumentType.CreditNote && x.SourceDocumentId == creditNote.Id, cancellationToken);

            glLines = glEntry?.Lines.Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
        }

        return new CreditNoteDetailDto(
            creditNote.Id,
            creditNote.OrganizationId,
            creditNote.ContactId,
            creditNote.Code,
            creditNote.Date,
            creditNote.Reference,
            creditNote.Status,
            creditNote.ApprovedByUserId,
            creditNote.ApprovedAt,
            creditNote.CreatedAt,
            creditNote.ReferrerType,
            creditNote.ReferrerId,
            creditNote.DiscountPct,
            creditNote.Terms,
            creditNote.Lines.Select(x => new CreditNoteLineDto(
                x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.VatAmount)).ToList(),
            glLines,
            creditNote.CurrencyCode,
            creditNote.ExchangeRate);
    }
}
