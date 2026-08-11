using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.GetJournalVoucher;

public sealed class GetJournalVoucherQueryHandler(IAppDbContext db)
    : IRequestHandler<GetJournalVoucherQuery, JournalVoucherDetailDto>
{
    public async Task<JournalVoucherDetailDto> Handle(GetJournalVoucherQuery request, CancellationToken cancellationToken)
    {
        var journalVoucher = await db.JournalVouchers
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Journal voucher not found.");

        IReadOnlyList<PostedGlLineDto>? glLines = null;

        if (journalVoucher.Status == JournalVoucherStatus.Approved)
        {
            var glEntry = await db.GlJournalEntries
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x => x.SourceDocumentType == DocumentType.JournalVoucher && x.SourceDocumentId == journalVoucher.Id,
                    cancellationToken);

            glLines = glEntry?.Lines.Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
        }

        return new JournalVoucherDetailDto(
            journalVoucher.Id,
            journalVoucher.OrganizationId,
            journalVoucher.Code,
            journalVoucher.Date,
            journalVoucher.Reference,
            journalVoucher.Status,
            journalVoucher.ApprovedByUserId,
            journalVoucher.ApprovedAt,
            journalVoucher.CreatedAt,
            journalVoucher.Lines.Select(x => new JournalVoucherLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList(),
            glLines);
    }
}
