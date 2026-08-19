using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.GetDebitNoteConversionTemplate;

public sealed class GetDebitNoteConversionTemplateQueryHandler(IAppDbContext db)
    : IRequestHandler<GetDebitNoteConversionTemplateQuery, DebitNoteConversionTemplateDto>
{
    public async Task<DebitNoteConversionTemplateDto> Handle(
        GetDebitNoteConversionTemplateQuery request, CancellationToken cancellationToken)
    {
        var purchaseBill = await db.PurchaseBills
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.PurchaseBillId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase bill not found.");

        if (purchaseBill.Status != PurchaseBillStatus.Approved)
        {
            throw new ConflictException("Only an Approved purchase bill can be converted to a Debit Note.");
        }

        var remainingByLine = await PurchasingValidation.GetPurchaseBillRemainingByLineAsync(
            db, request.OrganizationId, purchaseBill, cancellationToken);

        var lines = remainingByLine
            .Where(kv => kv.Value > 0)
            .Select(kv => new DebitNoteLineInput(kv.Key.ProductId, kv.Value, kv.Key.Rate, kv.Key.VatRate, kv.Key.DiscountPct))
            .ToList();

        if (lines.Count == 0)
        {
            throw new ConflictException("This purchase bill has already been fully debited.");
        }

        return new DebitNoteConversionTemplateDto(
            purchaseBill.ContactId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            $"From Purchase Bill {purchaseBill.Code}",
            purchaseBill.TdsTypeId,
            DocumentType.PurchaseBill,
            purchaseBill.Id,
            purchaseBill.DiscountPct,
            lines);
    }
}
