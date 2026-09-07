using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
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

        // Phase 28 -- see the Create handler's note. Draft-only, enforced by the aggregate.
        cashTransfer.SetCurrency(request.CurrencyCode, request.ExchangeRate);

        // Phase 32 -- same treatment as the currency pair above: resolved right after
        // construction rather than threaded through Create's parameter list. Null means
        // "the tenant's default", and LocationResolver returns a real null when this type is
        // outside the tenant's LocationScopeMode, so a client that keeps sending a location
        // after an Admin narrows the scope cannot quietly keep writing one.
        cashTransfer.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.CashTransfer, request.LocationId,
            cancellationToken));
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
