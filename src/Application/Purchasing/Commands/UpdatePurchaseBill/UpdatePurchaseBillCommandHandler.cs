using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.UpdatePurchaseBill;

public sealed class UpdatePurchaseBillCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdatePurchaseBillCommand, UpdatePurchaseBillResult>
{
    public async Task<UpdatePurchaseBillResult> Handle(UpdatePurchaseBillCommand request, CancellationToken cancellationToken)
    {
        var purchaseBill = await db.PurchaseBills
            .Include(x => x.Lines)
            .Include(x => x.AdditionalCosts)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase bill not found.");

        if (purchaseBill.Status != PurchaseBillStatus.Draft)
        {
            throw new ConflictException("Only a Draft purchase bill can be edited.");
        }

        await PurchasingValidation.EnsureSupplierExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);
        await PurchasingValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.WarehouseId, cancellationToken);
        await PurchasingValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var additionalCosts = request.AdditionalCosts ?? [];
        await PurchasingValidation.EnsureAdditionalCostsAreValidAsync(
            db, request.OrganizationId, additionalCosts, request.Lines.Select(x => x.ProductId), cancellationToken);

        var tdsBaseAmount = request.Lines.Sum(
            x => x.Quantity * x.Rate * (1 - x.DiscountPct / 100m) * (1 - request.DiscountPct / 100m));
        var tdsAmount = await PurchasingValidation.ResolveTdsAmountAsync(
            db, request.OrganizationId, request.TdsTypeId, tdsBaseAmount, cancellationToken);

        var oldLines = purchaseBill.Lines.ToList();
        var oldAdditionalCosts = purchaseBill.AdditionalCosts.ToList();

        purchaseBill.UpdateHeader(
            request.ContactId,
            request.WarehouseId,
            request.Date,
            request.Reference,
            request.SupplierInvoiceReference,
            request.IsImport,
            request.ImportCountry,
            request.ImportDate,
            request.ImportDocumentNo,
            request.TdsTypeId,
            tdsAmount,
            request.DiscountPct);

        // Phase 28 -- see the Create handler's note. Draft-only, enforced by the aggregate.
        purchaseBill.SetCurrency(request.CurrencyCode, request.ExchangeRate);

        purchaseBill.ClearLines();
        foreach (var line in request.Lines)
        {
            purchaseBill.AddLine(
                line.ProductId, line.Quantity, line.Rate, line.VatRate, line.ExpenditureClassification, line.DiscountPct);
        }

        // Phase 29 -- the same snapshot-then-RemoveRange/AddRange dance the lines need, for the
        // same reason (phase-4 bug #1: replacing an encapsulated child collection wholesale
        // mis-tracks on the InMemory provider).
        purchaseBill.SetProductWiseAdditionalCost(request.IsProductWiseAdditionalCost);
        purchaseBill.ClearAdditionalCosts();
        foreach (var cost in additionalCosts)
        {
            purchaseBill.AddAdditionalCost(cost.CostTermId, cost.ProductId, cost.Method, cost.Amount);
        }

        db.PurchaseBillLines.RemoveRange(oldLines);
        db.PurchaseBillLines.AddRange(purchaseBill.Lines);
        db.PurchaseBillAdditionalCosts.RemoveRange(oldAdditionalCosts);
        db.PurchaseBillAdditionalCosts.AddRange(purchaseBill.AdditionalCosts);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdatePurchaseBillResult(purchaseBill.Id, purchaseBill.Code, purchaseBill.Status);
    }
}
