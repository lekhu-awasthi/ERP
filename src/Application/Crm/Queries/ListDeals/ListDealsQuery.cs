using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Crm;
using MediatR;

namespace ErpApp.Application.Crm.Queries.ListDeals;

/// <summary>
/// ContactId is an optional filter (unlike ListTasksQuery's required ParentType/ParentId) -- Deal
/// is tied to a single Contact directly (ContactId), not a polymorphic (ParentType, ParentId) pair,
/// and the confirmed live UI shows Deals both scoped to one Contact (its own "Deals" tab) and
/// unscoped across the whole pipeline (the CRM module's own Deals screen / Organization dashboard
/// card) -- so ContactId narrows when supplied and is omitted for the org-wide view. Status is an
/// optional filter backing the 3 status tabs, same shape as ListTasksQuery's own Status? filter.
///
/// PermissionKey is PermissionKeys.DealView (Admin+Member) -- IsPrivate visibility is enforced
/// inside the handler itself (excluded unless the caller is the creator or one of the assignees),
/// not by this key, the same "blanket key for org-membership, real gating happens in the handler"
/// split ListTasksQuery established in Phase 13.
/// </summary>
public sealed record ListDealsQuery(
    Guid OrganizationId,
    Guid? ContactId,
    DealStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    // Phase 39 -- the standalone CRM > Deals screen has a search box, matching the live list. It
    // matches the deal's own title; the Contact column is searchable through the contact list, and
    // joining to match on it would make one screen's search mean something different from every
    // other screen's.
    string? Search = null,
    // Phase 43 (39 carried item #1) -- an optional single-row filter, so the new detail page reads
    // its record through the query that already knows how to shape one.
    //
    // <b>A filter rather than a GetXQuery, deliberately.</b> The row this returns is built from a
    // contact name, a lead-source name, a stage name and colour, and one user name per assignee; a
    // second query would be a second copy of that assembly, and phase-26b's rule is that two reads
    // agree by construction only when one reader answers for both. It also means the private-deal
    // visibility rule below -- enforced in the handler, not by the key -- covers the detail page on
    // day one rather than being something a new handler could forget.
    Guid? Id = null)
    : IRequest<DealListDto>, IRequirePermission, IOrganizationScoped, ISearchableQuery
{
    public string PermissionKey => PermissionKeys.DealView;
}

public sealed record DealAssigneeDto(Guid UserId, string Name);

public sealed record DealRowDto(
    Guid Id,
    string Title,
    Guid ContactId,
    string ContactName,
    Guid? LeadSourceId,
    string? LeadSourceName,
    string? Description,
    decimal ExpectedRevenue,
    DateOnly? ExpectedClosingDate,
    Guid? StageId,
    string? StageName,
    string? StageColor,
    DealStatus Status,
    bool IsPrivate,
    DateOnly? ClosingDate,
    Guid CreatedByUserId,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<DealAssigneeDto> Assignees);

public sealed record DealListDto(IReadOnlyList<DealRowDto> Rows, int Page, int PageSize, int TotalCount);
