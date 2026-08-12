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

        var remainingByLine = await SalesValidation.GetInvoiceRemainingByLineAsync(
            db, request.OrganizationId, invoice, cancellationToken);

        var lines = remainingByLine
            .Where(kv => kv.Value > 0)
            .Select(kv => new CreditNoteLineInput(kv.Key.ProductId, kv.Value, kv.Key.Rate, kv.Key.VatRate))
            .ToList();

        if (lines.Count == 0)
        {
            throw new ConflictException("This invoice has already been fully credited.");
        }

        return new CreditNoteConversionTemplateDto(
            invoice.ContactId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            $"From Invoice {invoice.Code}",
            DocumentType.Invoice,
            invoice.Id,
            lines);
    }
}
