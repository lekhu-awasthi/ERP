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
            var glEntry = await db.GlJournalEntries
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x => x.SourceDocumentType == DocumentType.CashTransfer && x.SourceDocumentId == cashTransfer.Id,
                    cancellationToken);

            glLines = glEntry?.Lines.Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
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
            glLines);
    }
}
