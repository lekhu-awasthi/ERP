using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Commands.DeleteBillOfMaterials;

/// <summary>
/// Refuses when a Production Order or Journal still points at this BOM. Those references are what
/// the Production Variance report compares against, so deleting the plan out from under a document
/// that recorded it would silently empty that report rather than fail loudly. Marking it inactive
/// is the way to retire a recipe that has already been used.
/// </summary>
public sealed class DeleteBillOfMaterialsCommandHandler(IAppDbContext db)
    : IRequestHandler<DeleteBillOfMaterialsCommand, Unit>
{
    public async Task<Unit> Handle(DeleteBillOfMaterialsCommand request, CancellationToken cancellationToken)
    {
        var bom = await db.BillsOfMaterials
            .Include(x => x.RawMaterials).Include(x => x.ByProducts).Include(x => x.Expenses)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Bill of materials not found.");

        var inUse = await db.ProductionOrders.AnyAsync(x => x.BillOfMaterialsId == request.Id, cancellationToken)
            || await db.ProductionJournals.AnyAsync(x => x.BillOfMaterialsId == request.Id, cancellationToken);

        if (inUse)
        {
            throw new ConflictException(
                "This bill of materials is used by a production order or production journal and cannot be deleted. " +
                "Mark it inactive instead.");
        }

        db.BomRawMaterialLines.RemoveRange(bom.RawMaterials.ToList());
        db.BomByProductLines.RemoveRange(bom.ByProducts.ToList());
        db.BomExpenseLines.RemoveRange(bom.Expenses.ToList());
        db.BillsOfMaterials.Remove(bom);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
