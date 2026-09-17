using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.GetInvoiceConversionTemplate;

public sealed class GetInvoiceConversionTemplateQueryHandler(IAppDbContext db)
    : IRequestHandler<GetInvoiceConversionTemplateQuery, InvoiceConversionTemplateDto>
{
    public async Task<InvoiceConversionTemplateDto> Handle(
        GetInvoiceConversionTemplateQuery request, CancellationToken cancellationToken)
    {
        var quotation = await db.Quotations
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.QuotationId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Quotation not found.");

        if (quotation.Status != QuotationStatus.Approved)
        {
            throw new ConflictException("Only an Approved quotation can be converted to an Invoice.");
        }

        var lines = quotation.Lines
            // Phase 52 -- the source line's unit rides the prefill. Phase 35a's rule is that
            // adding a field to many aggregates owes write, read and *every prefill between*, and
            // a template that drops it converts a Quotation for 2 cartons into an Invoice for 2
            // pieces, with the money unchanged and nothing on screen to show for it.
            .Select(x => new InvoiceLineInput(
                x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, UnitId: x.UnitId))
            .ToList();

        return new InvoiceConversionTemplateDto(
            quotation.ContactId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            $"From Quotation {quotation.Code}",
            DocumentType.Quotation,
            quotation.Id,
            quotation.DiscountPct,
            lines,
            quotation.LocationId,
            quotation.Terms);
    }
}
