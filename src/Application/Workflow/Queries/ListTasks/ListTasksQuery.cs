using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Application.Workflow.Queries.ListTasks;

/// <summary>
/// Tasks for one parent, or -- since phase 39 -- for the whole organization.
///
/// <para><b>Phase 13 called the org-wide feed speculative and refused to build it</b>: "not a
/// speculative 'all my tasks across the org' cross-entity feed, which erp-module-scan.md never
/// confirms exists". That was the right call on the evidence it had. Phase 34b's router-derived nav
/// then made the gap visible (its carried item #6), and the phase-39 live pass settled it:
/// <c>Workflow &gt; Tasks</c> is a real nav leaf, and its list shows tasks across every parent with
/// <b>no parent column at all</b> -- which is why this query gained a nullable parent rather than a
/// parent-kind filter. A null <see cref="ParentType"/> and <see cref="ParentId"/> mean "every task
/// in this organization the caller may see"; supplying both keeps the phase-13 behaviour exactly.</para>
///
/// <para>Status is an optional filter mirroring ListPurchaseOrders(organizationId, status?)'s precedent.</para>
///
/// PermissionKey is PermissionKeys.TaskView (Admin+Member) -- IsPrivate visibility is enforced
/// inside the handler itself (excluded unless the caller is the creator or assignee), not by this
/// key, the same "blanket key for org-membership, real gating happens in the handler" split Phase
/// 12's TransactionApprovalQuery established.
/// </summary>
public sealed record ListTasksQuery(
    Guid OrganizationId,
    TaskParentType? ParentType,
    Guid? ParentId,
    WorkTaskStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    // Phase 39 -- the standalone screen has a search box, matching the live list. It matches on the
    // task's own title, which is the only text a task row shows that a person would search by.
    string? Search = null)
    : IRequest<TaskListDto>, IRequirePermission, IOrganizationScoped, ISearchableQuery
{
    public string PermissionKey => PermissionKeys.TaskView;
}

public sealed record TaskRowDto(
    Guid Id,
    string Title,
    string? Description,
    DateOnly? DueDate,
    DateTimeOffset CreatedAt,
    Guid TaskTypeId,
    string TaskTypeName,
    string TaskTypeColor,
    TaskPriority Priority,
    WorkTaskStatus Status,
    bool IsPrivate,
    Guid CreatedByUserId,
    string CreatedByName,
    Guid? AssignedToUserId,
    string? AssignedToName);

public sealed record TaskListDto(IReadOnlyList<TaskRowDto> Rows, int Page, int PageSize, int TotalCount);
