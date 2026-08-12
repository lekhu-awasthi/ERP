using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.ListSalesOrders;

public sealed record ListSalesOrdersQuery(Guid OrganizationId, SalesOrderStatus? Status) : IRequest<IReadOnlyList<SalesOrder>>;
