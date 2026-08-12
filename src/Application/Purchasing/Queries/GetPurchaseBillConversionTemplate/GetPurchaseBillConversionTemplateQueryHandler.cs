using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.GetPurchaseBillConversionTemplate;

public sealed class GetPurchaseBillConversionTemplateQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPurchaseBillConversionTemplateQuery, PurchaseBillConversionTemplateDto>
{
    public async Task<PurchaseBillConversionTemplateDto> Handle(
        GetPurchaseBillConversionTemplateQuery request, CancellationToken cancellationToken)
    {
        var purchaseOrder = await db.PurchaseOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.PurchaseOrderId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase order not found.");

        if (purchaseOrder.Status != PurchaseOrderStatus.Approved)
        {
            throw new ConflictException("Only an Approved purchase order can be converted to a Purchase Bill.");
        }

        var lines = purchaseOrder.Lines
            .Select(x => new PurchaseBillLineInput(x.ProductId, x.Quantity, x.Rate, x.VatRate, ExpenditureClassification.Others))
            .ToList();

        return new PurchaseBillConversionTemplateDto(
            purchaseOrder.ContactId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            $"From Purchase Order {purchaseOrder.Code}",
            DocumentType.PurchaseOrder,
            purchaseOrder.Id,
            lines);
    }
}
