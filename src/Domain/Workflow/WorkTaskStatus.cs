namespace ErpApp.Domain.Workflow;

/// <summary>
/// erp-module-scan.md line 106's confirmed live "3 status tabs: Pending/Started/Done". Named
/// WorkTaskStatus, not the bare "TaskStatus" the product data model calls it -- see WorkTask's own
/// doc comment for why: System.Threading.Tasks.TaskStatus is a real BCL enum, implicitly in scope
/// in every async C# file in this codebase, so "TaskStatus" would collide the moment a handler file
/// needs both this enum and ErpApp.Domain.Workflow's own namespace.
///
/// Declared in Pending &lt; Started &lt; Done ordinal order deliberately -- WorkTask.TransitionStatus
/// compares ordinals directly to reject a backward move.
/// </summary>
public enum WorkTaskStatus
{
    Pending,
    Started,
    Done,
}
