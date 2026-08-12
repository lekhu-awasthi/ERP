using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.ListQuotations;

public sealed record ListQuotationsQuery(Guid OrganizationId, QuotationStatus? Status) : IRequest<IReadOnlyList<Quotation>>;
