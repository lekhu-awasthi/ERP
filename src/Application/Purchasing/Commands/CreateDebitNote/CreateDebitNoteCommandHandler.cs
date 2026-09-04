using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.CreateDebitNote;

public sealed class CreateDebitNoteCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateDebitNoteCommand, CreateDebitNoteResult>
{
    public async Task<CreateDebitNoteResult> Handle(CreateDebitNoteCommand request, CancellationToken cancellationToken)
    {
        await PurchasingValidation.EnsureSupplierExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);
        await PurchasingValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        if (request.ReferrerType == DocumentType.PurchaseBill && request.ReferrerId is { } purchaseBillId)
        {
            await PurchasingValidation.EnsureDebitNoteLinesWithinPurchaseBillRemainingAsync(
                db, request.OrganizationId, purchaseBillId, request.ContactId, request.TdsTypeId, request.DiscountPct, request.Lines,
                cancellationToken);
        }

        var tdsBaseAmount = request.Lines.Sum(
            x => x.Quantity * x.Rate * (1 - x.DiscountPct / 100m) * (1 - request.DiscountPct / 100m));
        var tdsAmount = await PurchasingValidation.ResolveTdsAmountAsync(
            db, request.OrganizationId, request.TdsTypeId, tdsBaseAmount, cancellationToken);

        var debitNote = DebitNote.Create(
            request.OrganizationId, request.ContactId, request.Date, request.Reference, request.TdsTypeId, tdsAmount,
            request.ReferrerType, request.ReferrerId, request.DiscountPct);

        // Phase 28 -- the currency pair is set right after construction rather than threaded
        // through Create's parameter list; see the aggregate's SetCurrency doc comment for why.
        // Null/null means the base currency at rate 1, so a caller that never heard of this phase
        // gets exactly the behaviour it had before.
        debitNote.SetCurrency(request.CurrencyCode, request.ExchangeRate);
        foreach (var line in request.Lines)
        {
            debitNote.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct);
        }

        db.DebitNotes.Add(debitNote);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateDebitNoteResult(debitNote.Id, debitNote.Code, debitNote.Status);
    }
}
