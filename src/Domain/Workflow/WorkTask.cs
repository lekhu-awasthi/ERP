namespace ErpApp.Domain.Workflow;

/// <summary>
/// architecture-spec.md §4.9 / product-requirements.md FR-10.1 -- a general-purpose polymorphic
/// task, attachable to a Contact or the Organization itself via (ParentType, ParentId) rather than
/// per-parent-type nullable FK columns, so a third parent type later is additive, not a schema
/// break. Named WorkTask, not the bare "Task" the product/erp-module-scan.md calls it --
/// System.Threading.Tasks.Task is implicitly in scope in every async C# file in this codebase
/// (ImplicitUsings), and every handler method returns Task&lt;TResult&gt;, so a Domain type literally
/// named "Task" would be ambiguous the instant a handler file also needs
/// ErpApp.Domain.Workflow's namespace. Every other layer (permission keys Workflow.TaskView/
/// TaskManage, table name Tasks, routes /tasks, DTOs, Angular) still uses plain "Task" throughout --
/// only this C# type and WorkTaskStatus are renamed.
///
/// No Draft/Approve lifecycle at all (unlike every ApprovableTransaction in this codebase) -- no
/// Code, no IDocumentNumberGenerator involvement, no DocumentNumberingRule row. Status is
/// Pending/Started/Done instead, and Update() is guarded by Status != Done (mirroring the
/// EnsureDraft() guard-method shape every ApprovableTransaction uses, just keyed off a different
/// terminal state).
/// </summary>
public sealed class WorkTask
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public TaskParentType ParentType { get; private set; }
    public Guid ParentId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid? AssignedToUserId { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public Guid TaskTypeId { get; private set; }
    public TaskPriority Priority { get; private set; }
    public WorkTaskStatus Status { get; private set; }
    public bool IsPrivate { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private WorkTask()
    {
    }

    public static WorkTask Create(
        Guid organizationId,
        TaskParentType parentType,
        Guid parentId,
        string title,
        string? description,
        Guid? assignedToUserId,
        DateOnly? dueDate,
        Guid taskTypeId,
        TaskPriority priority,
        bool isPrivate,
        Guid createdByUserId)
    {
        return new WorkTask
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ParentType = parentType,
            ParentId = parentId,
            Title = title,
            Description = description,
            AssignedToUserId = assignedToUserId,
            DueDate = dueDate,
            TaskTypeId = taskTypeId,
            Priority = priority,
            Status = WorkTaskStatus.Pending,
            IsPrivate = isPrivate,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        string title,
        string? description,
        Guid? assignedToUserId,
        DateOnly? dueDate,
        Guid taskTypeId,
        TaskPriority priority,
        bool isPrivate)
    {
        EnsureNotDone();
        Title = title;
        Description = description;
        AssignedToUserId = assignedToUserId;
        DueDate = dueDate;
        TaskTypeId = taskTypeId;
        Priority = priority;
        IsPrivate = isPrivate;
    }

    /// <summary>Only a strictly-forward move is legal (Pending -> Started, Pending -> Done,
    /// Started -> Done, comparing WorkTaskStatus's own declared ordinal order) -- the confirmed
    /// live UI only ever shows a forward-moving per-row complete checkmark (erp-module-scan.md),
    /// never a way to reopen a Done task or revert Started back to Pending, so backward/no-op
    /// transitions are rejected rather than silently supported on spec.</summary>
    public void TransitionStatus(WorkTaskStatus newStatus)
    {
        if (newStatus <= Status)
        {
            throw new InvalidOperationException($"Cannot move a task from {Status} to {newStatus}.");
        }

        Status = newStatus;
    }

    private void EnsureNotDone()
    {
        if (Status == WorkTaskStatus.Done)
        {
            throw new InvalidOperationException("A Done task can no longer be edited.");
        }
    }
}
