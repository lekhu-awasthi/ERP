using ErpApp.Application.Common.Persistence;
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
