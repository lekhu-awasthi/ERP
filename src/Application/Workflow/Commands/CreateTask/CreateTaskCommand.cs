using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Application.Workflow.Commands.CreateTask;

public sealed record CreateTaskCommand(
    Guid OrganizationId,
    TaskParentType ParentType,
    Guid ParentId,
    string Title,
    string? Description,
    Guid? AssignedToUserId,
    DateOnly? DueDate,
    Guid TaskTypeId,
    TaskPriority Priority,
    bool IsPrivate)
    : IRequest<CreateTaskResult>, IRequirePermission, IOrganizationScoped, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.TaskManage;

    // Phase 43 (39 carried item #1) -- the Activity tab on the new detail page is the audit feed,
    // and AuditBehavior only writes a row for a request that declares itself auditable. Without
    // this the tab would render, work, and be permanently empty.
    public DocumentType AuditDocumentType => DocumentType.WorkTask;
}

public sealed record CreateTaskResult(
    Guid Id, TaskParentType ParentType, Guid ParentId, string Title, WorkTaskStatus Status, DateTimeOffset CreatedAt);
