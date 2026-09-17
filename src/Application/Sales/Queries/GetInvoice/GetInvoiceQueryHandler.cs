using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.GetInvoice;

public sealed class GetInvoiceQueryHandler(IAppDbContext db) : IRequestHandler<GetInvoiceQuery, InvoiceDetailDto>
{
    public async Task<InvoiceDetailDto> Handle(GetInvoiceQuery request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");


        // Phase 51 -- the batch each line names and the serials it lists, read back through the one
        // shared reader so two detail queries cannot drift in how they answer the same question.
        var batches = await LineAllocationReader.LoadBatchesAsync(
            db, request.OrganizationId, invoice.Lines.Select(x => x.BatchId), cancellationToken);
        var serials = await LineAllocationReader.LoadSerialsAsync(
            db, request.OrganizationId, DocumentLineParentType.InvoiceLine,
            invoice.Lines.Select(x => x.Id).ToList(), cancellationToken);

        IReadOnlyList<PostedGlLineDto>? glLines = null;

        if (invoice.Status == InvoiceStatus.Approved)
        {
            // Phase 37 -- every entry this document posted, not "the" entry (phase 36's
            // finding, generalised): a cost catch-up rides on the same
            // (SourceDocumentType, SourceDocumentId) pair, and SingleOrDefaultAsync throws on
            // two rows exactly as SingleAsync does. The panel shows what the document really
            // did to the ledger, so it shows all of it.
            var glEntries = await SourceDocumentGlEntries.LoadAsync(
                db, DocumentType.Invoice, invoice.Id, cancellationToken);

            glLines = glEntries.Count == 0
                ? null
                : glEntries.SelectMany(e => e.Lines)
                    .Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
        }

        // Phase 52 -- the unit each line names, read back through the one shared reader so the
        // eight detail queries cannot drift in how they answer the same question.
        var unitNames = await DocumentLineUnitResolver.LoadUnitNamesAsync(
            db, request.OrganizationId, invoice.Lines.Select(x => x.UnitId), cancellationToken);

        return new InvoiceDetailDto(
            invoice.Id,
            invoice.OrganizationId,
            invoice.ContactId,
            invoice.WarehouseId,
            invoice.LocationId,
            invoice.Code,
            invoice.Date,
            invoice.DueDate,
            invoice.Reference,
            invoice.IsExport,
            invoice.ExportCountry,
            invoice.ExportDeclarationNo,
            invoice.ExportDeclarationDate,
            invoice.Status,
            invoice.ApprovedByUserId,
            invoice.ApprovedAt,
            invoice.CreatedAt,
            invoice.ReferrerType,
            invoice.ReferrerId,
            invoice.DiscountPct,
            invoice.GrandTotal,
            invoice.Terms,
            invoice.Lines.Select(x => new InvoiceLineDto(
                x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.VatAmount,
                x.BatchId is null ? null : batches.GetValueOrDefault(x.BatchId.Value)?.BatchNo,
                x.BatchId is null ? null : batches.GetValueOrDefault(x.BatchId.Value)?.ManufactureDate,
                x.BatchId is null ? null : batches.GetValueOrDefault(x.BatchId.Value)?.ExpiryDate,
                serials.TryGetValue(x.Id, out var lineSerials) ? lineSerials : [],
                x.UnitId, x.UnitId is null ? null : unitNames.GetValueOrDefault(x.UnitId.Value), x.ConversionFactor)).ToList(),
            glLines,
            invoice.CurrencyCode,
            invoice.ExchangeRate);
    }
}
