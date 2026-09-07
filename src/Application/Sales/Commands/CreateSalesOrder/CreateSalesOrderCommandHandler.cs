using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateSalesOrder;

public sealed class CreateSalesOrderCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateSalesOrderCommand, CreateSalesOrderResult>
{
    public async Task<CreateSalesOrderResult> Handle(CreateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        await SalesValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, ContactType.Customer, cancellationToken);
        await SalesValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var salesOrder = SalesOrder.Create(
            request.OrganizationId, request.ContactId, request.Date, request.DeliveryDate, request.Reference, request.DiscountPct);

        // Phase 28 -- the currency pair is set right after construction rather than threaded
        // through Create's parameter list; see the aggregate's SetCurrency doc comment for why.
        // Null/null means the base currency at rate 1, so a caller that never heard of this phase
        // gets exactly the behaviour it had before.
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

        foreach (var line in request.Lines)
        {
            salesOrder.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct);
        }

        db.SalesOrders.Add(salesOrder);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateSalesOrderResult(salesOrder.Id, salesOrder.Code, salesOrder.Status);
    }
}
