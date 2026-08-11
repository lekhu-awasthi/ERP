using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.UpdateCashTransfer;

public sealed class UpdateCashTransferCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateCashTransferCommand, UpdateCashTransferResult>
{
    public async Task<UpdateCashTransferResult> Handle(UpdateCashTransferCommand request, CancellationToken cancellationToken)
    {
        var cashTransfer = await db.CashTransfers
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Cash transfer not found.");

        if (cashTransfer.Status != CashTransferStatus.Draft)
        {
            throw new ConflictException("Only a Draft cash transfer can be edited.");
        }

        var accountIds = request.Lines.Select(x => x.ToAccountId).Append(request.FromAccountId);
        await AccountingValidation.EnsureAccountsExistAsync(db, request.OrganizationId, accountIds, cancellationToken);

        // Explicit DbSet Remove/Add for the replaced lines -- see
        // UpdateJournalVoucherCommandHandler's identical comment for why.
        var oldLines = cashTransfer.Lines.ToList();

        cashTransfer.UpdateHeader(request.Date, request.Reference, request.FromAccountId);
        cashTransfer.ClearLines();
        foreach (var line in request.Lines)
        {
            cashTransfer.AddLine(line.ToAccountId, line.Amount);
        }

        db.CashTransferLines.RemoveRange(oldLines);
        db.CashTransferLines.AddRange(cashTransfer.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateCashTransferResult(cashTransfer.Id, cashTransfer.Code, cashTransfer.Status);
    }
}
