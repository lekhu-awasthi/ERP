using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.UpdateInvoice;

public sealed class UpdateInvoiceCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateInvoiceCommand, UpdateInvoiceResult>
{
    public async Task<UpdateInvoiceResult> Handle(UpdateInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");

        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new ConflictException("Only a Draft invoice can be edited.");
        }

        await SalesValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, ContactType.Customer, cancellationToken);
        await SalesValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.WarehouseId, cancellationToken);
        await SalesValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var oldLines = invoice.Lines.ToList();

        // UpdateHeader applies the export flag before the lines are re-added, so AddLine's own
        // zero-rating sees the new flag rather than the previous save's.
        invoice.UpdateHeader(
            request.ContactId, request.WarehouseId, request.Date, request.Reference, request.DiscountPct,
            request.DueDate,
            request.IsExport, request.ExportCountry, request.ExportDeclarationNo, request.ExportDeclarationDate);

        // Phase 28 -- see the Create handler's note. Draft-only, enforced by the aggregate.
        invoice.SetCurrency(request.CurrencyCode, request.ExchangeRate);

        // Phase 32 -- same treatment as the currency pair above: resolved right after
        // construction rather than threaded through Create's parameter list. Null means
        // "the tenant's default", and LocationResolver returns a real null when this type is
        // outside the tenant's LocationScopeMode, so a client that keeps sending a location
        // after an Admin narrows the scope cannot quietly keep writing one.
        invoice.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.Invoice, request.LocationId,
            cancellationToken));
        invoice.SetTerms(request.Terms);
        invoice.ClearLines();
        foreach (var line in request.Lines)
        {
            invoice.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct);
        }

        db.InvoiceLines.RemoveRange(oldLines);
        db.InvoiceLines.AddRange(invoice.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateInvoiceResult(invoice.Id, invoice.Code, invoice.Status);
    }
}
