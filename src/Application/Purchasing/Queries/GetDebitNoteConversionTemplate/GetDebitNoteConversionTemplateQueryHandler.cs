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

        var lines = purchaseBill.Lines
            .Select(x => new DebitNoteLineInput(x.ProductId, x.Quantity, x.Rate, x.VatRate))
            .ToList();

        return new DebitNoteConversionTemplateDto(
            purchaseBill.ContactId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            $"From Purchase Bill {purchaseBill.Code}",
            purchaseBill.TdsTypeId,
            DocumentType.PurchaseBill,
            purchaseBill.Id,
            lines);
    }
}
