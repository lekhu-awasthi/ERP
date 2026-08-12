using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.ListPurchaseBills;

public sealed record ListPurchaseBillsQuery(Guid OrganizationId, PurchaseBillStatus? Status) : IRequest<IReadOnlyList<PurchaseBill>>;
