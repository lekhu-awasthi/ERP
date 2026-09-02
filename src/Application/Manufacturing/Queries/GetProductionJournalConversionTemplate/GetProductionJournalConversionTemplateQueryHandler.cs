using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Queries.GetProductionJournalConversionTemplate;

/// <summary>
/// Refuses anything but an Approved order, so the UI cannot offer a Convert action the Create
/// handler would then reject. That is a courtesy, not the enforcement -- ProductionOrder.
/// MarkConverted is the enforcement (phase-6 bug #4: a template that merely declines to prefill
/// stops nobody from posting the command directly).
///
/// <para>No Warehouse is carried across because a Production Order does not have one -- the user
/// picks it on the journal form, which is where stock actually moves.</para>
/// </summary>
public sealed class GetProductionJournalConversionTemplateQueryHandler(IAppDbContext db)
    : IRequestHandler<GetProductionJournalConversionTemplateQuery, ProductionJournalConversionTemplateDto>
{
    public async Task<ProductionJournalConversionTemplateDto> Handle(
        GetProductionJournalConversionTemplateQuery request, CancellationToken cancellationToken)
    {
        var order = await db.ProductionOrders
            .Include(x => x.RawMaterials).Include(x => x.ByProducts).Include(x => x.Expenses)
            .SingleOrDefaultAsync(
                x => x.Id == request.ProductionOrderId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Production order not found.");

        if (order.Status != ProductionOrderStatus.Approved)
        {
            throw new ConflictException(
                "This production order has already been converted to a Production Journal, or is not Approved.");
        }

        var productName = await db.Products
            .Where(x => x.Id == order.ProductId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        return new ProductionJournalConversionTemplateDto(
            order.Date,
            order.Reference,
            order.ProductId,
            productName,
            order.OutputQuantity,
            order.BillOfMaterialsId,
            order.Notes,
            DocumentType.ProductionOrder,
            order.Id,
            [.. order.RawMaterials.Select(x => new ProductionRawMaterialLineInput(x.ProductId, x.Quantity))],
            [.. order.ByProducts.Select(x => new ProductionByProductLineInput(x.ProductId, x.CostAllocationPct, x.Quantity))],
            [.. order.Expenses.Select(x => new ProductionExpenseLineInput(x.CostTermId, x.Amount))]);
    }
}
