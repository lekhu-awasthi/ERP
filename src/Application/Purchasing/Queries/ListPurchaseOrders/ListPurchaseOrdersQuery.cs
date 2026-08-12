using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.ListPurchaseOrders;

public sealed record ListPurchaseOrdersQuery(Guid OrganizationId, PurchaseOrderStatus? Status) : IRequest<IReadOnlyList<PurchaseOrder>>;
