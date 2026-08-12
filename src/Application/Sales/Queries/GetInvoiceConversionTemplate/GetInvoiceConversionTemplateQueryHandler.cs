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
            .Select(x => new InvoiceLineInput(x.ProductId, x.Quantity, x.Rate, x.VatRate))
            .ToList();

        return new InvoiceConversionTemplateDto(
            quotation.ContactId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            $"From Quotation {quotation.Code}",
            DocumentType.Quotation,
            quotation.Id,
            lines);
    }
}
