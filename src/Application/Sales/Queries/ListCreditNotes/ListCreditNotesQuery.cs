using ErpApp.Application.Common.Pagination;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.ListCreditNotes;

public sealed record ListCreditNotesQuery(
    Guid OrganizationId,
    CreditNoteStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<CreditNote>>;
