using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Application.Workflow.Queries.ListTasks;

/// <summary>
/// Parent-scoped only (required ParentType/ParentId, not optional) -- matching this codebase's
/// established bounded-scope discipline (Phase 10's Contact Overview), not a speculative "all my
/// tasks across the org" cross-entity feed, which erp-module-scan.md never confirms exists.
/// Status is an optional filter mirroring ListPurchaseOrders(organizationId, status?)'s precedent.
///
/// PermissionKey is PermissionKeys.TaskView (Admin+Member) -- IsPrivate visibility is enforced
/// inside the handler itself (excluded unless the caller is the creator or assignee), not by this
/// key, the same "blanket key for org-membership, real gating happens in the handler" split Phase
/// 12's TransactionApprovalQuery established.
/// </summary>
public sealed record ListTasksQuery(
    Guid OrganizationId,
    TaskParentType ParentType,
    Guid ParentId,
    WorkTaskStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<TaskListDto>, IRequirePermission, IOrganizationScoped
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
