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
                db, request.OrganizationId, purchaseBillId, request.ContactId, request.TdsTypeId, request.Lines, cancellationToken);
        }

        var tdsBaseAmount = request.Lines.Sum(x => x.Quantity * x.Rate);
        var tdsAmount = await PurchasingValidation.ResolveTdsAmountAsync(
            db, request.OrganizationId, request.TdsTypeId, tdsBaseAmount, cancellationToken);

        var debitNote = DebitNote.Create(
            request.OrganizationId, request.ContactId, request.Date, request.Reference, request.TdsTypeId, tdsAmount,
            request.ReferrerType, request.ReferrerId);
        foreach (var line in request.Lines)
        {
            debitNote.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate);
        }

        db.DebitNotes.Add(debitNote);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateDebitNoteResult(debitNote.Id, debitNote.Code, debitNote.Status);
    }
}
