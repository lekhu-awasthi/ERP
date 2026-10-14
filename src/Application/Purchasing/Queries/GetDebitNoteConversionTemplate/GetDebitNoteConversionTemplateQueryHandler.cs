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

        var lines = new List<DebitNoteLineInput>();
        foreach (var (key, remaining) in remainingByLine.Where(kv => kv.Value > 0))
        {
            var candidates = purchaseBill.Lines
                .Where(l => (l.ProductId, l.Rate, l.VatRate, l.DiscountPct, l.UnitId) == key)
                .SelectMany(l => receivedSerials.GetValueOrDefault(l.Id) ?? [])
                .Where(serial => !returnedSerials.Contains((key.ProductId, serial.ToUpperInvariant())))
                .ToList();
            var onShelf = candidates.Count == 0
                ? []
                : await SerialStock.InStockAsync(
                    db, request.OrganizationId, key.ProductId, candidates, cancellationToken, purchaseBill.WarehouseId);

            // Phase 52 -- see GetCreditNoteConversionTemplateQueryHandler: the unit is part of the
            // conversion-cap key, so the remaining quantity is denominated in it.
            lines.Add(new DebitNoteLineInput(
                key.ProductId, remaining, key.Rate, key.VatRate, key.DiscountPct,
                UnitId: key.UnitId,
                SerialNumbers: [.. candidates.Where(s => onShelf.Contains(s, StringComparer.OrdinalIgnoreCase))]));
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
