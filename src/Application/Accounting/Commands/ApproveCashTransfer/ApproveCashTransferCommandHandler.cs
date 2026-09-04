using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.ApproveCashTransfer;

public sealed class ApproveCashTransferCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<CashTransfer> postingRule)
    : IRequestHandler<ApproveCashTransferCommand, ApproveCashTransferResult>
{
    public async Task<ApproveCashTransferResult> Handle(ApproveCashTransferCommand request, CancellationToken cancellationToken)
    {
        var cashTransfer = await db.CashTransfers
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Cash transfer not found.");

        if (cashTransfer.Status != CashTransferStatus.Draft)
        {
            throw new ConflictException("Only a Draft cash transfer can be approved.");
        }

        if (cashTransfer.Lines.Count < 1)
        {
            throw new ConflictException("A cash transfer needs at least one destination line to be approved.");
        }

        if (cashTransfer.Lines.Any(x => x.ToAccountId == cashTransfer.FromAccountId))
        {
            throw new ConflictException("A cash transfer's destination accounts must differ from its From account.");
        }

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.CashTransfer, cancellationToken);

        cashTransfer.Approve(currentUser.UserId, code);

        // Phase 28 -- see ApproveJournalVoucherCommandHandler's note; the same fallback path, for
        // the same reason.
        var glLines = await GlCurrencyConversion.ToBaseAsync(
            db, request.OrganizationId, postingRule.BuildLines(cashTransfer), cashTransfer.ExchangeRate,
            cancellationToken);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.CashTransfer, cashTransfer.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveCashTransferResult(cashTransfer.Id, cashTransfer.Code, cashTransfer.Status, cashTransfer.ApprovedAt);
    }
}
