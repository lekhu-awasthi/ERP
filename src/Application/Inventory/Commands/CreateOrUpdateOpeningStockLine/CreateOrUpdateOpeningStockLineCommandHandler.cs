using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Commands.CreateOrUpdateOpeningStockLine;

/// <summary>
/// Editing an existing line reverses its own prior FIFO layer first (IStockLedgerService.
/// ReverseIncrementAsync) before incrementing the corrected one -- throws a 409 if that original
/// layer has already been partly consumed by a later real transaction, same protection every other
/// document type's Void gets (not a new invariant). Both calls are dated at the Organization's own
/// AccountingStartDate, the confirmed "day zero" framing for opening balances.
/// </summary>
public sealed class CreateOrUpdateOpeningStockLineCommandHandler(IAppDbContext db, IStockLedgerService stockLedger)
    : IRequestHandler<CreateOrUpdateOpeningStockLineCommand, OpeningStockLineResult>
{
    public async Task<OpeningStockLineResult> Handle(
        CreateOrUpdateOpeningStockLineCommand request, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(
            x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        if (!product.TrackInventory)
        {
            throw new ConflictException("This product does not track inventory.");
        }

        // Phase 24: the fourth and last sweep call site -- this handler reads its single product
        // directly rather than through an Ensure...Async helper. See ProductVariantRules.
        ProductVariantRules.EnsureTransactable(product.Name, product.HasVariants);

        var warehouseExists = await db.Warehouses.AnyAsync(
            x => x.Id == request.WarehouseId && x.OrganizationId == request.OrganizationId, cancellationToken);
        if (!warehouseExists)
        {
            throw new NotFoundException("Warehouse not found.");
        }

        var organization = await db.Organizations.SingleAsync(x => x.Id == request.OrganizationId, cancellationToken);

        var line = await db.OpeningStockLines.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId && x.ProductId == request.ProductId
                && x.WarehouseId == request.WarehouseId,
            cancellationToken);

        if (line is not null)
        {
            await stockLedger.ReverseIncrementAsync(
                request.OrganizationId, DocumentType.OpeningStock, line.Id, organization.AccountingStartDate, cancellationToken);

            line.Update(request.Quantity, request.Rate);
        }
        else
        {
            line = OpeningStockLine.Create(request.OrganizationId, request.ProductId, request.WarehouseId, request.Quantity, request.Rate);
            db.OpeningStockLines.Add(line);
        }

        await stockLedger.IncrementAsync(
            request.OrganizationId, request.ProductId, request.WarehouseId, request.Quantity, request.Rate,
            DocumentType.OpeningStock, line.Id, organization.AccountingStartDate, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new OpeningStockLineResult(line.Id, line.ProductId, line.WarehouseId, line.Quantity, line.Rate);
    }
}
