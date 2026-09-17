using ErpApp.Application.Common.Documents;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;

public sealed class CreatePurchaseBillCommandHandler(IAppDbContext db)
    : IRequestHandler<CreatePurchaseBillCommand, CreatePurchaseBillResult>
{
    public async Task<CreatePurchaseBillResult> Handle(CreatePurchaseBillCommand request, CancellationToken cancellationToken)
    {
        await PurchasingValidation.EnsureSupplierExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);
        await PurchasingValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.WarehouseId, cancellationToken);
        await PurchasingValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var additionalCosts = request.AdditionalCosts ?? [];
        await PurchasingValidation.EnsureAdditionalCostsAreValidAsync(
            db, request.OrganizationId, additionalCosts, request.Lines.Select(x => x.ProductId), cancellationToken);

        if (request.ReferrerType == DocumentType.PurchaseOrder && request.ReferrerId is { } purchaseOrderId)
        {
            var purchaseOrder = await db.PurchaseOrders.SingleOrDefaultAsync(
                x => x.Id == purchaseOrderId && x.OrganizationId == request.OrganizationId, cancellationToken)
                ?? throw new NotFoundException("Purchase order not found.");

            if (purchaseOrder.Status != PurchaseOrderStatus.Approved)
            {
                throw new ConflictException("This purchase order has already been converted to a Purchase Bill, or is not Approved.");
            }

            purchaseOrder.MarkConverted();
        }

        // TDS base is the pre-VAT taxable amount net of both line and header discount (matching
        // VAT's own base -- discount reduces the taxable base before either tax is computed), same
        // base every other tax computation in this codebase uses (see phase-6-status.md's scope
        // decisions for why this base was chosen).
        var tdsBaseAmount = request.Lines.Sum(
            x => x.Quantity * x.Rate * (1 - x.DiscountPct / 100m) * (1 - request.DiscountPct / 100m));
        var tdsAmount = await PurchasingValidation.ResolveTdsAmountAsync(
            db, request.OrganizationId, request.TdsTypeId, tdsBaseAmount, cancellationToken);

        // Phase 36 -- the contact's Credit Term applied here rather than only in the browser, so a
        // document raised through the API, an import or a conversion gets the same due date the
        // form would have prefilled. An explicit DueDate always wins. See DueDateResolver.
        var dueDate = await DueDateResolver.ResolveAsync(
            db, request.OrganizationId, request.ContactId, request.Date, request.DueDate, cancellationToken);

        var purchaseBill = PurchaseBill.Create(
            request.OrganizationId,
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
            request.ReferrerType,
            request.ReferrerId,
            request.DiscountPct,
            dueDate);

        // Phase 28 -- the currency pair is set right after construction rather than threaded
        // through Create's parameter list; see the aggregate's SetCurrency doc comment for why.
        // Null/null means the base currency at rate 1, so a caller that never heard of this phase
        // gets exactly the behaviour it had before.
        purchaseBill.SetCurrency(request.CurrencyCode, request.ExchangeRate);

        // Phase 32 -- same treatment as the currency pair above: resolved right after
        // construction rather than threaded through Create's parameter list. Null means
        // "the tenant's default", and LocationResolver returns a real null when this type is
        // outside the tenant's LocationScopeMode, so a client that keeps sending a location
        // after an Admin narrows the scope cannot quietly keep writing one.
        purchaseBill.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.PurchaseBill, request.LocationId,
            cancellationToken));

        // Phase 51 -- resolve (and, on a receipt, mint) the batch each line names, then add the
        // lines carrying the ids, then write the serial rows against the line ids AddLine minted.
        // The order matters: a line id does not exist until AddLine has run, and the serial rows are
        // keyed by it.
        var allocations = await DocumentLineAllocationWriter.ResolveBatchesAsync(
            db, request.OrganizationId,
            request.Lines.Select(x => new DocumentLineAllocationWriter.LineAllocationInput(
                x.ProductId, x.Quantity, x.BatchNo, x.ManufactureDate, x.ExpiryDate, x.SerialNumbers)).ToList(),
            isReceipt: true, cancellationToken);

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
            purchaseBill.AddLine(
                line.ProductId, line.Quantity, line.Rate, line.VatRate, line.ExpenditureClassification,
                line.DiscountPct, units[i].UnitId, units[i].ConversionFactor, allocations[i]);
        }

        await DocumentLineAllocationWriter.ReplaceSerialsAsync(
            db, request.OrganizationId, DocumentLineParentType.PurchaseBillLine,
            [],
            purchaseBill.Lines.Select((l, i) => (l.Id, request.Lines[i].SerialNumbers)).ToList(),
            cancellationToken);

        // Phase 29 (FR-6.15) -- the Additional Cost section. Stored as entered; nothing is
        // allocated or capitalised until Approve, the same "side effects happen at Approve, not
        // Create" rule every document type here follows.
        purchaseBill.SetProductWiseAdditionalCost(request.IsProductWiseAdditionalCost);
        foreach (var cost in additionalCosts)
        {
            purchaseBill.AddAdditionalCost(cost.CostTermId, cost.ProductId, cost.Method, cost.Amount);
        }

        db.PurchaseBills.Add(purchaseBill);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatePurchaseBillResult(purchaseBill.Id, purchaseBill.Code, purchaseBill.Status);
    }
}
