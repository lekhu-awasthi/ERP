using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.ListCreditNotes;

public sealed record ListCreditNotesQuery(Guid OrganizationId, CreditNoteStatus? Status) : IRequest<IReadOnlyList<CreditNote>>;
