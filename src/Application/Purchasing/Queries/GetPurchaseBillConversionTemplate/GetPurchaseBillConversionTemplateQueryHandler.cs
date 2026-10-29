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

        // Phase 99 -- the prefill carries each line's credit terms as a new line would get them (Taxable, the product's
        // Rule 41 category), so the form shows what Save will store (phase 35a: every prefill between).
        var categories = await PurchaseLineVatCredit.ProductCategoriesAsync(
            db, request.OrganizationId, [.. purchaseOrder.Lines.Select(x => x.ProductId)], cancellationToken);
        var lines = purchaseOrder.Lines
            // Phase 52 -- see GetInvoiceConversionTemplateQueryHandler for why the unit rides
            // the prefill rather than being left to the target form's default.
            .Select(x => new PurchaseBillLineInput(
                x.ProductId, x.Quantity, x.Rate, x.VatRate, ExpenditureClassification.Others, x.DiscountPct,
                UnitId: x.UnitId,
                CreditUse: VatCreditUse.Taxable,
                CreditCategory: categories.GetValueOrDefault(x.ProductId)))
            .ToList();

        return new PurchaseBillConversionTemplateDto(
            purchaseOrder.ContactId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            $"From Purchase Order {purchaseOrder.Code}",
            DocumentType.PurchaseOrder,
            purchaseOrder.Id,
            purchaseOrder.DiscountPct,
            lines,
            purchaseOrder.LocationId,
            purchaseOrder.CurrencyCode,
            purchaseOrder.ExchangeRate);
    }
}
