using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.ListDebitNotes;

public sealed record ListDebitNotesQuery(
    Guid OrganizationId,
    DebitNoteStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<DebitNote>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.DebitNoteView;
}
