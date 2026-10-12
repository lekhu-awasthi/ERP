using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Payments.Allocation;
using MediatR;

namespace ErpApp.Application.Payments.Queries.ListOpenAllocationTargets;

public sealed class ListOpenAllocationTargetsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListOpenAllocationTargetsQuery, IReadOnlyList<OpenAllocationTargetDto>>
{
    public Task<IReadOnlyList<OpenAllocationTargetDto>> Handle(
        ListOpenAllocationTargetsQuery request, CancellationToken cancellationToken) =>
        OpenAllocationTargets.LoadAsync(
            db, currentUser, request.OrganizationId, request.ContactId, request.Direction, request.CurrencyCode,
            cancellationToken);
}
