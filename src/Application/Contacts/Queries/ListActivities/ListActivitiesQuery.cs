using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.ListActivities;

/// <summary>
/// Real, auto-generated activity feed (docs/phase-18-status.md decision #3) -- reads Audit rows
/// filtered by DocumentType=Contact/DocumentId=ContactId, exactly the reuse Audit's own doc comment
/// anticipated ("this same behavior also backs the future Contact/Organization/Product 'Activity'
/// tab"). Phase 18 is the first caller: it wires CreateContact/UpdateContact and Create/
/// UpdateContactPersonnel up to AuditBehavior (see those commands' own IAuditableRequest[WithId]
/// implementations). Deliberately does NOT include WorkTask-completed or Deal-stage-changed events
/// -- both are out of scope to modify this phase ("Don't touch Tasks or Deals" -- Phase 18 kickoff
/// scope guard), so those event types are a known, explicitly stated limitation, not silently
/// dropped.
/// </summary>
public sealed record ListActivitiesQuery(Guid OrganizationId, Guid ContactId, int Page = 1, int PageSize = PagingDefaults.MaxPageSize)
    : IRequest<ActivityListDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactView;
}

public sealed record ActivityRowDto(Guid Id, string Action, Guid UserId, string UserName, DateTimeOffset CreatedAt);

public sealed record ActivityListDto(IReadOnlyList<ActivityRowDto> Rows, int Page, int PageSize, int TotalCount);
