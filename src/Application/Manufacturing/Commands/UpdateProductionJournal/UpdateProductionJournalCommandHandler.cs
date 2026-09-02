using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Manufacturing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Commands.UpdateProductionJournal;

public sealed class UpdateProductionJournalCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateProductionJournalCommand, UpdateProductionJournalResult>
{
    public async Task<UpdateProductionJournalResult> Handle(
        UpdateProductionJournalCommand request, CancellationToken cancellationToken)
    {
        var journal = await db.ProductionJournals
            .Include(x => x.RawMaterials).Include(x => x.ByProducts).Include(x => x.Expenses)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Production journal not found.");

        if (journal.Status != ProductionJournalStatus.Draft)
        {
            throw new ConflictException("Only a Draft production journal can be edited.");
        }

        var productIds = ProductionRequestProducts.Collect(request.ProductId, request.RawMaterials, request.ByProducts);

        await ManufacturingValidation.EnsureProductsExistAsync(db, request.OrganizationId, productIds, cancellationToken);
        await ManufacturingValidation.EnsureProductsAreGoodsAsync(db, request.OrganizationId, productIds, cancellationToken);
        await ManufacturingValidation.EnsureWarehouseExistsAsync(
            db, request.OrganizationId, request.WarehouseId, cancellationToken);
        await ManufacturingValidation.EnsureCostTermsAreProductionCostsAsync(
            db, request.OrganizationId, request.Expenses.Select(x => x.CostTermId), cancellationToken);
        await ManufacturingValidation.EnsureBillOfMaterialsExistsAsync(
            db, request.OrganizationId, request.BillOfMaterialsId, cancellationToken);

        db.ProductionJournalRawMaterialLines.RemoveRange(journal.RawMaterials.ToList());
        db.ProductionJournalByProductLines.RemoveRange(journal.ByProducts.ToList());
        db.ProductionJournalExpenseLines.RemoveRange(journal.Expenses.ToList());

        journal.UpdateHeader(
            request.Date, request.Reference, request.ProductId, request.OutputQuantity, request.WarehouseId,
            request.BillOfMaterialsId, request.Notes);
        journal.ClearLines();
        ProductionLineWriter.Fill(journal, request.RawMaterials, request.ByProducts, request.Expenses);
        journal.EnsureByProductAllocationIsSane();

        db.ProductionJournalRawMaterialLines.AddRange(journal.RawMaterials.ToList());
        db.ProductionJournalByProductLines.AddRange(journal.ByProducts.ToList());
        db.ProductionJournalExpenseLines.AddRange(journal.Expenses.ToList());

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateProductionJournalResult(journal.Id, journal.Code, journal.Status);
    }
}
