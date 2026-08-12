using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.ListInvoices;

public sealed record ListInvoicesQuery(Guid OrganizationId, InvoiceStatus? Status) : IRequest<IReadOnlyList<Invoice>>;
