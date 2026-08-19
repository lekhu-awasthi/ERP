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
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<DealListDto>, IRequirePermission, IOrganizationScoped
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
