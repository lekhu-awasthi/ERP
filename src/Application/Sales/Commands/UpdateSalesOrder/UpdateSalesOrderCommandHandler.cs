using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.UpdateSalesOrder;

public sealed class UpdateSalesOrderCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UpdateSalesOrderCommand, UpdateSalesOrderResult>
{
    public async Task<UpdateSalesOrderResult> Handle(UpdateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var salesOrder = await db.SalesOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Sales order not found.");

        if (salesOrder.Status != SalesOrderStatus.Draft)
        {
            throw new ConflictException("Only a Draft sales order can be edited.");
        }

        await SalesValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, ContactType.Customer, cancellationToken);
        await SalesValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var oldLines = salesOrder.Lines.ToList();
        var carriedDiscountPct = salesOrder.DiscountPct;

        salesOrder.UpdateHeader(request.ContactId, request.Date, request.DeliveryDate, request.Reference, request.DiscountPct);

        // Phase 28 -- see the Create handler's note. Draft-only, enforced by the aggregate.
        salesOrder.SetCurrency(request.CurrencyCode, request.ExchangeRate);

        // Phase 32 -- same treatment as the currency pair above: resolved right after
        // construction rather than threaded through Create's parameter list. Null means
        // "the tenant's default", and LocationResolver returns a real null when this type is
        // outside the tenant's LocationScopeMode, so a client that keeps sending a location
        // after an Admin narrows the scope cannot quietly keep writing one.
        salesOrder.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.SalesOrder, request.LocationId,
            cancellationToken));
        salesOrder.SetTerms(request.Terms);

        salesOrder.ClearLines();
        // Phase 52 -- the catalogue is read here and never again: ResolveAsync turns the unit
        // each line names into the factor frozen onto it, so editing (or deleting) the product's
        // unit row afterwards cannot reach back and change what this document did to stock.
        var units = await DocumentLineUnitResolver.ResolveAsync(
            db, request.OrganizationId,
            [.. request.Lines.Select(x => new DocumentLineUnitResolver.LineUnitInput(x.ProductId, x.UnitId))],
            cancellationToken);

        // Phase 81 -- a discount or a changed rate needs its key at this sales order's location; unless the
        // draft already carried it (SalesPriceGate).
        await SalesPriceGate.EnsureMayPriceAsync(
            db, request.OrganizationId, currentUser.UserId, salesOrder.LocationId, SalesPriceGate.IsBase(salesOrder.CurrencyCode),
            request.DiscountPct,
            [.. request.Lines.Select((x, i) => new SalesPriceGate.PricedLine(x.ProductId, units[i].UnitId, x.Rate, x.DiscountPct))],
            carriedDiscountPct,
            [.. oldLines.Select(x => new SalesPriceGate.PricedLine(x.ProductId, x.UnitId, x.Rate, x.DiscountPct))],
            cancellationToken);

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            salesOrder.AddLine(
                line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct,
                units[i].UnitId, units[i].ConversionFactor);
        }

        db.SalesOrderLines.RemoveRange(oldLines);
        db.SalesOrderLines.AddRange(salesOrder.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateSalesOrderResult(salesOrder.Id, salesOrder.Code, salesOrder.Status);
    }
}
