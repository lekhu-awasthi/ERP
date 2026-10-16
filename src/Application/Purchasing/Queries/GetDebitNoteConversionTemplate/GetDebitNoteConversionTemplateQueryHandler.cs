using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.GetDebitNoteConversionTemplate;

public sealed class GetDebitNoteConversionTemplateQueryHandler(IAppDbContext db)
    : IRequestHandler<GetDebitNoteConversionTemplateQuery, DebitNoteConversionTemplateDto>
{
    public async Task<DebitNoteConversionTemplateDto> Handle(
        GetDebitNoteConversionTemplateQuery request, CancellationToken cancellationToken)
    {
        var purchaseBill = await db.PurchaseBills
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.PurchaseBillId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase bill not found.");

        if (purchaseBill.Status != PurchaseBillStatus.Approved)
        {
            throw new ConflictException("Only an Approved purchase bill can be converted to a Debit Note.");
        }

        var remainingByLine = await PurchasingValidation.GetPurchaseBillRemainingByLineAsync(
            db, request.OrganizationId, purchaseBill, cancellationToken);

        // Phase 92 -- the units each key received that are still on the bill's shelf and no approved note sent back,
        // prefilled as the credit note's template prefills the units still out (phase 91). A unit sold since is not
        // offered: it is with a customer, and the line's count under the serial box says how many are left to name.
        var receivedSerials = await LineStockAllocator.LoadSerialsAsync(
            db, request.OrganizationId, DocumentLineParentType.PurchaseBillLine, [.. purchaseBill.Lines.Select(x => x.Id)],
            cancellationToken);
        HashSet<(Guid ProductId, string SerialNo)> returnedSerials = receivedSerials.Count == 0
            ? []
            : await DebitNoteSerials.ReturnedAsync(db, request.OrganizationId, purchaseBill.Id, null, cancellationToken);

        // Phase 93 -- the batch each line received, as the vendor's form prefills it (read live 2026-10-15): a key's
        // remaining quantity is offered batch by batch, in the bill's line order, each batch at most what the bill
        // received of it less what approved notes sent back under it. Nothing wrote a note's batch before this phase.
        var batchNumbers = await LineAllocationReader.LoadBatchesAsync(
            db, request.OrganizationId, purchaseBill.Lines.Select(x => x.BatchId), cancellationToken);
        var returnedByBatch = await (
                from line in db.DebitNoteLines
                join note in db.DebitNotes on line.DebitNoteId equals note.Id
                where note.OrganizationId == request.OrganizationId && note.ReferrerType == DocumentType.PurchaseBill
                    && note.ReferrerId == purchaseBill.Id && note.Status == DebitNoteStatus.Approved
                    && line.BatchId != null
                select new { line.ProductId, line.Rate, line.VatRate, line.DiscountPct, line.UnitId, line.BatchId, line.Quantity })
            .ToListAsync(cancellationToken);

        var lines = new List<DebitNoteLineInput>();
        foreach (var (key, remaining) in remainingByLine.Where(kv => kv.Value > 0))
        {
            var keyLines = purchaseBill.Lines
                .Where(l => (l.ProductId, l.Rate, l.VatRate, l.DiscountPct, l.UnitId) == key)
                .ToList();
            var candidates = keyLines
                .SelectMany(l => (receivedSerials.GetValueOrDefault(l.Id) ?? []).Select(s => (l.BatchId, Serial: s)))
                .Where(x => !returnedSerials.Contains((key.ProductId, x.Serial.ToUpperInvariant())))
                .ToList();
            var onShelf = candidates.Count == 0
                ? []
                : await SerialStock.InStockAsync(
                    db, request.OrganizationId, key.ProductId, [.. candidates.Select(x => x.Serial)], cancellationToken,
                    purchaseBill.WarehouseId);

            var left = remaining;
            // By batch number: a bill's lines load in no fixed order, and a prefill that reshuffles on every read is
            // not one a clerk can check against the paper.
            foreach (var batch in keyLines
                .GroupBy(l => l.BatchId)
                .OrderBy(g => g.Key is null ? string.Empty : batchNumbers.GetValueOrDefault(g.Key.Value)?.BatchNo,
                    StringComparer.OrdinalIgnoreCase))
            {
                var returned = returnedByBatch
                    .Where(r => (r.ProductId, r.Rate, r.VatRate, r.DiscountPct, r.UnitId) == key && r.BatchId == batch.Key)
                    .Sum(r => r.Quantity);
                var take = batch.Key is null ? left : Math.Min(left, batch.Sum(l => l.Quantity) - returned);
                if (take <= 0)
                {
                    continue;
                }

                left -= take;

                // Phase 52 -- see GetCreditNoteConversionTemplateQueryHandler: the unit is part of the
                // conversion-cap key, so the remaining quantity is denominated in it.
                lines.Add(new DebitNoteLineInput(
                    key.ProductId, take, key.Rate, key.VatRate, key.DiscountPct,
                    UnitId: key.UnitId,
                    SerialNumbers: [.. candidates
                        .Where(x => x.BatchId == batch.Key && onShelf.Contains(x.Serial, StringComparer.OrdinalIgnoreCase))
                        .Select(x => x.Serial)],
                    BatchNo: batch.Key is null ? null : batchNumbers.GetValueOrDefault(batch.Key.Value)?.BatchNo));
                if (left <= 0)
                {
                    break;
                }
            }
        }

        if (lines.Count == 0)
        {
            throw new ConflictException("This purchase bill has already been fully debited.");
        }

        return new DebitNoteConversionTemplateDto(
            purchaseBill.ContactId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            $"From Purchase Bill {purchaseBill.Code}",
            purchaseBill.TdsTypeId,
            DocumentType.PurchaseBill,
            purchaseBill.Id,
            purchaseBill.DiscountPct,
            lines,
            purchaseBill.LocationId,
            purchaseBill.WarehouseId,
            purchaseBill.CurrencyCode,
            purchaseBill.ExchangeRate);
    }
}
