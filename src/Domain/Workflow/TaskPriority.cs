namespace ErpApp.Domain.Workflow;

/// <summary>
/// erp-module-scan.md line 107's confirmed Task.priority values. No competing tenant-configurable
/// lookup-screen evidence anywhere in the scan (unlike Type/TaskType -- see WorkTask's own doc
/// comment), so this stays a plain enum.
/// </summary>
public enum TaskPriority
{
    Normal,
    Urgent,
}
