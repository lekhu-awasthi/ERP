using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;

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

        // TDS base is the pre-VAT taxable amount, same base every other tax computation in this
        // codebase uses (see phase-6-status.md's scope decisions for why this base was chosen).
        var tdsBaseAmount = request.Lines.Sum(x => x.Quantity * x.Rate);
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
            request.ReferrerId);

        foreach (var line in request.Lines)
        {
            purchaseBill.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate, line.ExpenditureClassification);
        }

        db.PurchaseBills.Add(purchaseBill);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatePurchaseBillResult(purchaseBill.Id, purchaseBill.Code, purchaseBill.Status);
    }
}
