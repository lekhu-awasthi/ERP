using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.GetCreditNoteConversionTemplate;

public sealed class GetCreditNoteConversionTemplateQueryHandler(IAppDbContext db)
    : IRequestHandler<GetCreditNoteConversionTemplateQuery, CreditNoteConversionTemplateDto>
{
    public async Task<CreditNoteConversionTemplateDto> Handle(
        GetCreditNoteConversionTemplateQuery request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.InvoiceId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");

        if (invoice.Status != InvoiceStatus.Approved)
        {
            throw new ConflictException("Only an Approved invoice can be converted to a Credit Note.");
        }

        var lines = invoice.Lines
            .Select(x => new CreditNoteLineInput(x.ProductId, x.Quantity, x.Rate, x.VatRate))
            .ToList();

        return new CreditNoteConversionTemplateDto(
            invoice.ContactId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            $"From Invoice {invoice.Code}",
            DocumentType.Invoice,
            invoice.Id,
            lines);
    }
}
