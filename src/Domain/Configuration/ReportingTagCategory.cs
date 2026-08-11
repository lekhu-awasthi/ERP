using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Tenant-scoped grouping for ReportingTagOption (architecture-spec.md §3.8), e.g. a category
/// "Project" holding options "Project A"/"Project B". Referenced by Quotation/Invoice forms and
/// Reports filters from Phase 4+ (not yet).
/// </summary>
public sealed class ReportingTagCategory : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ReportingTagCategory()
    {
    }

    public static ReportingTagCategory Create(Guid organizationId, string name)
    {
        return new ReportingTagCategory
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, bool isActive)
    {
        Name = name;
        IsActive = isActive;
    }
}
