using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.DeleteProductVariant;

public sealed class DeleteProductVariantCommandHandler(IAppDbContext db)
    : IRequestHandler<DeleteProductVariantCommand, Unit>
{
    public async Task<Unit> Handle(DeleteProductVariantCommand request, CancellationToken cancellationToken)
    {
        var variant = await db.Products
            .Include(x => x.VariantValues)
            .SingleOrDefaultAsync(
                x => x.Id == request.VariantId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product variant not found.");

        if (variant.ParentProductId is not { } parentId)
        {
            throw new ConflictException("That product is not a variant.");
        }

        if (await HasBeenTransactedAsync(variant.Id, cancellationToken))
        {
            throw new ConflictException(
                "This variant has been used on a document or holds stock, so it cannot be deleted. Deactivate it instead.");
        }

        db.ProductVariantValues.RemoveRange(variant.VariantValues);
        db.Products.Remove(variant);

        // Demote the parent back to an ordinary (transactable) product once its last variant goes.
        var parent = await db.Products
            .Include(x => x.VariantAttributeUsages)
            .SingleAsync(x => x.Id == parentId, cancellationToken);

        var remaining = await db.Products.CountAsync(
            x => x.ParentProductId == parentId && x.Id != variant.Id, cancellationToken);

        if (remaining == 0)
        {
            parent.ClearHasVariants();
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    /// <summary>Every place a ProductId can be recorded. Written as explicit per-table checks
    /// rather than a generic helper over a selector Func -- EF Core cannot translate a captured
    /// delegate inside Where (CLAUDE.md's generic-Func gotcha, phase-9 bug #1).</summary>
    private async Task<bool> HasBeenTransactedAsync(Guid productId, CancellationToken cancellationToken)
    {
        return await db.StockLedgerEntries.AnyAsync(x => x.ProductId == productId, cancellationToken)
            || await db.StockMovements.AnyAsync(x => x.ProductId == productId, cancellationToken)
            || await db.OpeningStockLines.AnyAsync(x => x.ProductId == productId, cancellationToken)
            || await db.InvoiceLines.AnyAsync(x => x.ProductId == productId, cancellationToken)
            || await db.CreditNoteLines.AnyAsync(x => x.ProductId == productId, cancellationToken)
            || await db.QuotationLines.AnyAsync(x => x.ProductId == productId, cancellationToken)
            || await db.SalesOrderLines.AnyAsync(x => x.ProductId == productId, cancellationToken)
            || await db.PurchaseBillLines.AnyAsync(x => x.ProductId == productId, cancellationToken)
            || await db.PurchaseOrderLines.AnyAsync(x => x.ProductId == productId, cancellationToken)
            || await db.DebitNoteLines.AnyAsync(x => x.ProductId == productId, cancellationToken)
            || await db.WarehouseTransferLines.AnyAsync(x => x.ProductId == productId, cancellationToken)
            || await db.InventoryAdjustmentLines.AnyAsync(x => x.ProductId == productId, cancellationToken)
            || await db.ProductSecondaryUnits.AnyAsync(x => x.ProductId == productId, cancellationToken);
    }
}
