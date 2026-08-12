using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.ListDebitNotes;

public sealed record ListDebitNotesQuery(Guid OrganizationId, DebitNoteStatus? Status) : IRequest<IReadOnlyList<DebitNote>>;
