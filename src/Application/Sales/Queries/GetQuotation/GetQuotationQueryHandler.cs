using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.GetQuotation;

public sealed class GetQuotationQueryHandler(IAppDbContext db) : IRequestHandler<GetQuotationQuery, QuotationDetailDto>
{
    public async Task<QuotationDetailDto> Handle(GetQuotationQuery request, CancellationToken cancellationToken)
    {
        var quotation = await db.Quotations
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Quotation not found.");

        // Phase 52 -- the unit each line names, read back through the one shared reader so the
        // eight detail queries cannot drift in how they answer the same question.
        var unitNames = await DocumentLineUnitResolver.LoadUnitNamesAsync(
            db, request.OrganizationId, quotation.Lines.Select(x => x.UnitId), cancellationToken);

        return new QuotationDetailDto(
            quotation.Id,
            quotation.OrganizationId,
            quotation.ContactId,
            quotation.Code,
            quotation.Date,
            quotation.ExpiryDate,
            quotation.Reference,
            quotation.Status,
            quotation.ApprovedByUserId,
            quotation.ApprovedAt,
            quotation.CreatedAt,
            quotation.DiscountPct,
            quotation.CustomStatusId,
            quotation.Terms,
            quotation.Lines.Select(x => new QuotationLineDto(
                x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.VatAmount,
                x.UnitId, x.UnitId is null ? null : unitNames.GetValueOrDefault(x.UnitId.Value), x.ConversionFactor)).ToList(),
            quotation.CurrencyCode,
            quotation.ExchangeRate,
            quotation.LocationId);
    }
}
