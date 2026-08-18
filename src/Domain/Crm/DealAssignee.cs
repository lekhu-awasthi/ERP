namespace ErpApp.Domain.Crm;

/// <summary>
/// Child entity of Deal (architecture-spec.md §4.2) -- one row per assigned user, a genuine
/// many-to-many shape (erp-module-scan.md's "Assigned To (multi-avatar)" list column), unlike
/// WorkTask's single scalar AssignedToUserId. Own table, no aggregate-root behavior of its own --
/// created/removed only via Deal.AddAssignee/RemoveAssignee.
/// </summary>
public sealed class DealAssignee
{
    public Guid Id { get; private set; }
    public Guid DealId { get; private set; }
    public Guid UserId { get; private set; }

    private DealAssignee()
    {
    }

    internal static DealAssignee Create(Guid dealId, Guid userId)
    {
        return new DealAssignee
        {
            Id = Guid.NewGuid(),
            DealId = dealId,
            UserId = userId,
        };
    }
}
