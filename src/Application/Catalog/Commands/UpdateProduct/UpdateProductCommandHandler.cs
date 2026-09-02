using ErpApp.Application.Accounting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.UpdateProduct;

public sealed class UpdateProductCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateProductCommand, UpdateProductResult>
{
    public async Task<UpdateProductResult> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        var categoryExists = await db.ProductCategories.AnyAsync(
            x => x.Id == request.CategoryId && x.OrganizationId == request.OrganizationId, cancellationToken);

        if (!categoryExists)
        {
            throw new NotFoundException("Product category not found.");
        }

        var unitExists = await db.UnitsOfMeasurement.AnyAsync(
            x => x.Id == request.PrimaryUnitId && x.OrganizationId == request.OrganizationId, cancellationToken);

        if (!unitExists)
        {
            throw new NotFoundException("Primary unit of measurement not found.");
        }

        var accountIds = new[]
            {
                request.SalesAccountId, request.SalesReturnAccountId, request.PurchaseAccountId, request.PurchaseReturnAccountId,
            }
            .Where(x => x is not null)
            .Select(x => x!.Value);

        await AccountingValidation.EnsureAccountsExistAsync(db, request.OrganizationId, accountIds, cancellationToken);

        product.Update(
            request.Name,
            request.CategoryId,
            request.PrimaryUnitId,
            request.HsCode,
            request.AvailableForSale,
            request.SellingPrice,
            request.PurchasePrice,
            request.VatRate,
            request.ReOrderLevel,
            request.TrackInventory,
            request.IsActive,
            request.Sku,
            request.Barcode);
        product.SetAccounts(
            request.SalesAccountId, request.SalesReturnAccountId, request.PurchaseAccountId, request.PurchaseReturnAccountId);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateProductResult(product.Id, product.Name);
    }
}
