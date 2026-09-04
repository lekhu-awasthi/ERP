using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.UpdateDebitNote;

public sealed class UpdateDebitNoteCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateDebitNoteCommand, UpdateDebitNoteResult>
{
    public async Task<UpdateDebitNoteResult> Handle(UpdateDebitNoteCommand request, CancellationToken cancellationToken)
    {
        var debitNote = await db.DebitNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Debit note not found.");

        if (debitNote.Status != DebitNoteStatus.Draft)
        {
            throw new ConflictException("Only a Draft debit note can be edited.");
        }

        await PurchasingValidation.EnsureSupplierExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);
        await PurchasingValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var tdsBaseAmount = request.Lines.Sum(
            x => x.Quantity * x.Rate * (1 - x.DiscountPct / 100m) * (1 - request.DiscountPct / 100m));
        var tdsAmount = await PurchasingValidation.ResolveTdsAmountAsync(
            db, request.OrganizationId, request.TdsTypeId, tdsBaseAmount, cancellationToken);

        var oldLines = debitNote.Lines.ToList();

        debitNote.UpdateHeader(request.ContactId, request.Date, request.Reference, request.TdsTypeId, tdsAmount, request.DiscountPct);

        // Phase 28 -- see the Create handler's note. Draft-only, enforced by the aggregate.
        debitNote.SetCurrency(request.CurrencyCode, request.ExchangeRate);
        debitNote.ClearLines();
        foreach (var line in request.Lines)
        {
            debitNote.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct);
        }

        db.DebitNoteLines.RemoveRange(oldLines);
        db.DebitNoteLines.AddRange(debitNote.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateDebitNoteResult(debitNote.Id, debitNote.Code, debitNote.Status);
    }
}
