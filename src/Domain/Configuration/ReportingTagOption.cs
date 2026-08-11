using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// A single tag value under a ReportingTagCategory (architecture-spec.md §3.8). The spec's
/// "Value" field is carried by the shared ITenantLookupEntity.Name property -- Name already means
/// "the display value" for every other lookup in this context, so a separate Value column would
/// be a redundant alias.
/// </summary>
public sealed class ReportingTagOption : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public Guid CategoryId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ReportingTagOption()
    {
    }

    public static ReportingTagOption Create(Guid organizationId, string name, Guid categoryId)
    {
        return new ReportingTagOption
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            CategoryId = categoryId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, Guid categoryId, bool isActive)
    {
        Name = name;
        CategoryId = categoryId;
        IsActive = isActive;
    }
}
