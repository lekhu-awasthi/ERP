using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.CreateDebitNote;

public sealed class CreateDebitNoteCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateDebitNoteCommand, CreateDebitNoteResult>
{
    public async Task<CreateDebitNoteResult> Handle(CreateDebitNoteCommand request, CancellationToken cancellationToken)
    {
        await PurchasingValidation.EnsureSupplierExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);
        await PurchasingValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        if (request.ReferrerType == DocumentType.PurchaseBill && request.ReferrerId is { } purchaseBillId)
        {
            await PurchasingValidation.EnsureDebitNoteLinesWithinPurchaseBillRemainingAsync(
                db, request.OrganizationId, purchaseBillId, request.ContactId, request.TdsTypeId, request.DiscountPct, request.Lines,
                cancellationToken);
        }

        // Phase 43 (37 carried item #1) -- where a Goods line's stock is returned from. Null on a
        // conversion falls back to the source bill's own warehouse, which is the value
        // ApproveDebitNoteCommandHandler used to read directly: the fallback is what makes the new
        // "approve consumes at the note's warehouse" rule a single rule rather than a branch, and
        // what keeps every client that predates this phase behaving exactly as it did.
        var warehouseId = request.WarehouseId ?? await ResolveSourceBillWarehouseAsync(
            db, request.OrganizationId, request.ReferrerType, request.ReferrerId, cancellationToken);

        await PurchasingValidation.EnsureDebitNoteWarehouseForGoodsAsync(
            db, request.OrganizationId, warehouseId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var tdsBaseAmount = request.Lines.Sum(
            x => x.Quantity * x.Rate * (1 - x.DiscountPct / 100m) * (1 - request.DiscountPct / 100m));
        var tdsAmount = await PurchasingValidation.ResolveTdsAmountAsync(
            db, request.OrganizationId, request.TdsTypeId, tdsBaseAmount, cancellationToken);

        var debitNote = DebitNote.Create(
            request.OrganizationId, request.ContactId, request.Date, request.Reference, request.TdsTypeId, tdsAmount,
            request.ReferrerType, request.ReferrerId, request.DiscountPct, warehouseId);

        // Phase 28 -- the currency pair is set right after construction rather than threaded
        // through Create's parameter list; see the aggregate's SetCurrency doc comment for why.
        // Null/null means the base currency at rate 1, so a caller that never heard of this phase
        // gets exactly the behaviour it had before.
        debitNote.SetCurrency(request.CurrencyCode, request.ExchangeRate);

        // Phase 32 -- same treatment as the currency pair above: resolved right after
        // construction rather than threaded through Create's parameter list. Null means
        // "the tenant's default", and LocationResolver returns a real null when this type is
        // outside the tenant's LocationScopeMode, so a client that keeps sending a location
        // after an Admin narrows the scope cannot quietly keep writing one.
        debitNote.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.DebitNote, request.LocationId,
            cancellationToken));
        // Phase 52 -- the catalogue is read here and never again: ResolveAsync turns the unit
        // each line names into the factor frozen onto it, so editing (or deleting) the product's
        // unit row afterwards cannot reach back and change what this document did to stock.
        var units = await DocumentLineUnitResolver.ResolveAsync(
            db, request.OrganizationId,
            [.. request.Lines.Select(x => new DocumentLineUnitResolver.LineUnitInput(x.ProductId, x.UnitId))],
            cancellationToken);

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            debitNote.AddLine(
                line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct,
                units[i].UnitId, units[i].ConversionFactor);
        }

        db.DebitNotes.Add(debitNote);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateDebitNoteResult(debitNote.Id, debitNote.Code, debitNote.Status);
    }

    /// <summary>The source Purchase Bill's warehouse, or null when this note is standalone.</summary>
    private static async Task<Guid?> ResolveSourceBillWarehouseAsync(
        IAppDbContext db, Guid organizationId, DocumentType? referrerType, Guid? referrerId,
        CancellationToken cancellationToken)
    {
        if (referrerType != DocumentType.PurchaseBill || referrerId is not { } purchaseBillId)
        {
            return null;
        }

        return await db.PurchaseBills
            .Where(x => x.Id == purchaseBillId && x.OrganizationId == organizationId)
            .Select(x => (Guid?)x.WarehouseId)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
