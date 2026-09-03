using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.ListActivities;

/// <summary>
/// Real, auto-generated activity feed (docs/phase-18-status.md decision #3) -- reads Audit rows
/// filtered by (DocumentType, DocumentId), exactly the reuse Audit's own doc comment anticipated
/// ("this same behavior also backs the future Contact/Organization/Product 'Activity' tab").
///
/// <para><b>Phase 27a took the DocumentType out of the handler and into the request.</b> Phase 18
/// hardcoded <c>DocumentType.Contact</c>, because the Contact tab was the only caller; the Activity
/// tab now exists on every transactional detail page too, and its Activities sub-tab is the same
/// audit feed keyed to that document. Nothing else changed -- <c>AuditBehavior</c> has been writing
/// rows for the transactional Create/Update/Approve commands since Phase 16d, so this feed had a
/// backing store on day one and needed no new writes.</para>
///
/// <para>Still deliberately does NOT include WorkTask-completed or Deal-stage-changed events -- the
/// Phase 18 limitation, unchanged and still explicitly stated rather than silently dropped.</para>
/// </summary>
public sealed record ListActivitiesQuery(
    Guid OrganizationId,
    DocumentType DocumentType,
    Guid DocumentId,
    int Page = 1,
    int PageSize = PagingDefaults.MaxPageSize)
    : IRequest<ActivityListDto>, IRequirePermission, IOrganizationScoped
{
    // Contact predates the View/Create/Edit/Approve split and keeps its own key; every document type
    // resolves through the shared map. DocumentPermissions throws for a DocumentType nothing can be
    // attached to, which is what keeps this from becoming a way to read arbitrary audit rows.
    public string PermissionKey => DocumentType == DocumentType.Contact
        ? PermissionKeys.ContactView
        : DocumentPermissions.ViewPermissionFor(DocumentType);
}

public sealed record ActivityRowDto(Guid Id, string Action, Guid UserId, string UserName, DateTimeOffset CreatedAt);

public sealed record ActivityListDto(IReadOnlyList<ActivityRowDto> Rows, int Page, int PageSize, int TotalCount);
