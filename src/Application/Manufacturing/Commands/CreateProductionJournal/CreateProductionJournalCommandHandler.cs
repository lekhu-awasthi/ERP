using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Commands.CreateProductionJournal;

/// <summary>
/// The fourth conversion target in this codebase, and the first built with phase-6 bug #4's lesson
/// applied from the start rather than retrofitted: setting ReferrerType/ReferrerId enforces
/// nothing, so the source order is loaded and MarkConverted() called, which refuses anything that
/// is not still Approved. The reference product does <i>not</i> do this -- its own PRO0011 still
/// offered "Convert to Production Journal" after PJ0013 had been created from it -- so this is a
/// deliberate divergence.
/// </summary>
public sealed class CreateProductionJournalCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateProductionJournalCommand, CreateProductionJournalResult>
{
    public async Task<CreateProductionJournalResult> Handle(
        CreateProductionJournalCommand request, CancellationToken cancellationToken)
    {
        var productIds = ProductionRequestProducts.Collect(request.ProductId, request.RawMaterials, request.ByProducts);

        await ManufacturingValidation.EnsureProductsExistAsync(db, request.OrganizationId, productIds, cancellationToken);
        await ManufacturingValidation.EnsureProductsAreGoodsAsync(db, request.OrganizationId, productIds, cancellationToken);
        await ManufacturingValidation.EnsureWarehouseExistsAsync(
            db, request.OrganizationId, request.WarehouseId, cancellationToken);
        await ManufacturingValidation.EnsureCostTermsAreProductionCostsAsync(
            db, request.OrganizationId, request.Expenses.Select(x => x.CostTermId), cancellationToken);
        await ManufacturingValidation.EnsureBillOfMaterialsExistsAsync(
            db, request.OrganizationId, request.BillOfMaterialsId, cancellationToken);

        if (request.ReferrerType == DocumentType.ProductionOrder && request.ReferrerId is { } productionOrderId)
        {
            var order = await db.ProductionOrders.SingleOrDefaultAsync(
                x => x.Id == productionOrderId && x.OrganizationId == request.OrganizationId, cancellationToken)
                ?? throw new NotFoundException("Production order not found.");

            if (order.Status != ProductionOrderStatus.Approved)
            {
                throw new ConflictException(
                    "This production order has already been converted to a Production Journal, or is not Approved.");
            }

            order.MarkConverted();
        }

        var journal = ProductionJournal.Create(
            request.OrganizationId, request.Date, request.Reference, request.ProductId, request.OutputQuantity,
            request.WarehouseId, request.BillOfMaterialsId, request.Notes, request.ReferrerType, request.ReferrerId);

        ProductionLineWriter.Fill(journal, request.RawMaterials, request.ByProducts, request.Expenses);
        journal.EnsureByProductAllocationIsSane();

        db.ProductionJournals.Add(journal);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateProductionJournalResult(journal.Id, journal.Code, journal.Status);
    }
}
