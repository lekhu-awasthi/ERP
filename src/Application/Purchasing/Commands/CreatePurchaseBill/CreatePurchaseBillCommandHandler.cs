using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
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
            request.DiscountPct);

        foreach (var line in request.Lines)
        {
            purchaseBill.AddLine(
                line.ProductId, line.Quantity, line.Rate, line.VatRate, line.ExpenditureClassification, line.DiscountPct);
        }

        db.PurchaseBills.Add(purchaseBill);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatePurchaseBillResult(purchaseBill.Id, purchaseBill.Code, purchaseBill.Status);
    }
}
