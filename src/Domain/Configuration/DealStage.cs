using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// CRM (config) > Deal Stage (erp-module-scan.md line 311-312): {id, name, sortOrder, color?}.
/// Modeled as a real tenant-editable lookup entity, same "confirmed dedicated management screen ->
/// generic lookup entity" precedent TaskType established in Phase 13 -- see Deal's own doc comment.
///
/// SortOrder is for display ordering on the config screen only, not an enforced state-machine
/// sequence -- a Deal's Stage is a plain inline dropdown while Pending (erp-module-scan.md's
/// confirmed live UI), not a per-row forward-only checkmark the way WorkTaskStatus is. See
/// Deal.MoveToStage's doc comment.
/// </summary>
public sealed class DealStage : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public string? Color { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private DealStage()
    {
    }

    public static DealStage Create(Guid organizationId, string name, int sortOrder, string? color)
    {
        return new DealStage
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            SortOrder = sortOrder,
            Color = color,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, int sortOrder, string? color, bool isActive)
    {
        Name = name;
        SortOrder = sortOrder;
        Color = color;
        IsActive = isActive;
    }
}
