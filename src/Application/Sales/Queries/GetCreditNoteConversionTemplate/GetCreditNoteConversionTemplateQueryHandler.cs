using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
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

        SalesValidation.EnsureNotTillSale(invoice);

        var remainingByLine = await SalesValidation.GetInvoiceRemainingByLineAsync(
            db, request.OrganizationId, invoice, cancellationToken);

        // Phase 91 -- the units each key sold that no approved note has brought back, prefilled as the vendor's
        // return form shows the sale's serials (editable: a customer may bring back fewer than they bought).
        var soldSerials = await LineStockAllocator.LoadSerialsAsync(
            db, request.OrganizationId, DocumentLineParentType.InvoiceLine, [.. invoice.Lines.Select(x => x.Id)],
            cancellationToken);
        HashSet<(Guid ProductId, string SerialNo)> returnedSerials = soldSerials.Count == 0
            ? []
            : await CreditNoteSerials.ReturnedAsync(db, request.OrganizationId, invoice.Id, null, cancellationToken);

        // Phase 93 -- a batch-tracked key is offered batch by batch, as the vendor's return form prefills the sale line's
        // batch (read live 2026-10-15): what the invoice issued of each batch less what notes returning it brought back,
        // the reader CreditNoteBatches caps by and the approval restocks from, so the three cannot disagree.
        var productIds = invoice.Lines.Select(x => x.ProductId).Distinct().ToList();
        var batchTracked = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id) && x.BatchTracking)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var outstanding = batchTracked.Count == 0
            ? []
            : await IssuedStockRestock.OutstandingOnInvoiceAsync(
                db, request.OrganizationId, invoice.Id, null, cancellationToken);
        var batchNumbers = await LineAllocationReader.LoadBatchesAsync(
            db, request.OrganizationId, outstanding.Select(x => x.BatchId), cancellationToken);
        var offered = new Dictionary<(Guid ProductId, Guid? BatchId), decimal>();

        var lines = new List<CreditNoteLineInput>();
        foreach (var (key, remaining) in remainingByLine.Where(kv => kv.Value > 0))
        {
            var keyLines = invoice.Lines.Where(l => SaleLineKey.Of(l) == key).ToList();
            var serials = keyLines
                .SelectMany(l => soldSerials.GetValueOrDefault(l.Id) ?? [])
                .Where(serial => !returnedSerials.Contains((key.ProductId, serial)))
                .ToList();

            if (!batchTracked.Contains(key.ProductId))
            {
                // Phase 52 -- the unit is part of the conversion-cap key now, so kv.Key carries it and
                // the remaining quantity is denominated in it. Without it the cap would add cartons to
                // pieces and credit a quantity in neither.
                lines.Add(new CreditNoteLineInput(
                    key.ProductId, remaining, key.Rate, key.VatRate, key.DiscountPct,
                    UnitId: key.UnitId, SerialNumbers: serials));
                continue;
            }

            // A line that named its batch offers that one; one that walked (a sale from before the batch was required)
            // offers whatever its product's walk took, in the order it was taken.
            var factor = keyLines[0].ConversionFactor;
            // In the order the sale took them (the movements'), since an invoice's lines load in no fixed order.
            var taken = outstanding
                .Where(x => x.ProductId == key.ProductId && x.BatchId is not null)
                .Select(x => x.BatchId)
                .Distinct()
                .ToList();
            var named = keyLines.Where(l => l.BatchId is not null).Select(l => l.BatchId).Distinct().ToList();
            var batches = keyLines.All(l => l.BatchId is not null)
                ? [.. named.OrderBy(b => taken.IndexOf(b) is var i and >= 0 ? i : int.MaxValue)]
                : taken;

            var left = remaining;
            foreach (var batchId in batches)
            {
                var available = outstanding
                    .Where(x => x.ProductId == key.ProductId && x.BatchId == batchId)
                    .Sum(x => x.Quantity) - offered.GetValueOrDefault((key.ProductId, batchId));
                var take = Math.Min(left, Math.Floor(available / factor * 10_000m) / 10_000m);
                if (take <= 0)
                {
                    continue;
                }

                offered[(key.ProductId, batchId)] = offered.GetValueOrDefault((key.ProductId, batchId)) + take * factor;
                left -= take;
                var batchSerials = outstanding
                    .Where(x => x.ProductId == key.ProductId && x.BatchId == batchId && x.SerialNo is not null)
                    .Select(x => x.SerialNo!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                lines.Add(new CreditNoteLineInput(
                    key.ProductId, take, key.Rate, key.VatRate, key.DiscountPct,
                    UnitId: key.UnitId,
                    SerialNumbers: [.. serials.Where(batchSerials.Contains)],
                    BatchNo: batchNumbers.GetValueOrDefault(batchId!.Value)?.BatchNo));
                if (left <= 0)
                {
                    break;
                }
            }

            if (left > 0)
            {
                // Stock the movements do not cover (issued before phase 51 stamped batches): offered with no batch, for
                // the user to name, as the vendor's form leaves a blank for them to fill.
                lines.Add(new CreditNoteLineInput(
                    key.ProductId, left, key.Rate, key.VatRate, key.DiscountPct,
                    UnitId: key.UnitId, SerialNumbers: []));
            }
        }

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
            invoice.DiscountPct,
            lines,
            invoice.LocationId,
            invoice.Terms,
            invoice.CurrencyCode,
            invoice.ExchangeRate);
    }
}
