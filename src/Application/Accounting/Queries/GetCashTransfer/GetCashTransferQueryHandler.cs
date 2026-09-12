using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.GetCashTransfer;

public sealed class GetCashTransferQueryHandler(IAppDbContext db)
    : IRequestHandler<GetCashTransferQuery, CashTransferDetailDto>
{
    public async Task<CashTransferDetailDto> Handle(GetCashTransferQuery request, CancellationToken cancellationToken)
    {
        var cashTransfer = await db.CashTransfers
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Cash transfer not found.");

        IReadOnlyList<PostedGlLineDto>? glLines = null;

        if (cashTransfer.Status == CashTransferStatus.Approved)
        {
            // Phase 37 -- every entry this document posted, not "the" entry (phase 36's
            // finding, generalised): a cost catch-up rides on the same
            // (SourceDocumentType, SourceDocumentId) pair, and SingleOrDefaultAsync throws on
            // two rows exactly as SingleAsync does. The panel shows what the document really
            // did to the ledger, so it shows all of it.
            var glEntries = await SourceDocumentGlEntries.LoadAsync(
                db, DocumentType.CashTransfer, cashTransfer.Id, cancellationToken);

            glLines = glEntries.Count == 0
                ? null
                : glEntries.SelectMany(e => e.Lines)
                    .Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
        }

        return new CashTransferDetailDto(
            cashTransfer.Id,
            cashTransfer.OrganizationId,
            cashTransfer.Code,
            cashTransfer.Date,
            cashTransfer.Reference,
            cashTransfer.FromAccountId,
            cashTransfer.Status,
            cashTransfer.ApprovedByUserId,
            cashTransfer.ApprovedAt,
            cashTransfer.CreatedAt,
            cashTransfer.Lines.Select(x => new CashTransferLineDto(x.Id, x.ToAccountId, x.Amount)).ToList(),
            glLines,
            cashTransfer.CurrencyCode,
            cashTransfer.ExchangeRate,
            cashTransfer.LocationId);
    }
}
