using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// EAV field definition (architecture-spec.md §3.6): one definition can apply to any subset of
/// document types. Definition CRUD only -- the generic CustomFieldValue rows this defines are not
/// written/read anywhere yet (no real document type exists before Phase 4+); rendering "custom
/// fields inline on a form" is explicitly deferred, per the roadmap.
/// </summary>
public sealed class CustomFieldDefinition
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public CustomFieldType Type { get; private set; }
    public IReadOnlyList<DocumentType> ApplicableDocumentTypes { get; private set; } = new List<DocumentType>();
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CustomFieldDefinition()
    {
    }

    public static CustomFieldDefinition Create(
        Guid organizationId, string name, CustomFieldType type, IEnumerable<DocumentType> applicableDocumentTypes)
    {
        return new CustomFieldDefinition
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Type = type,
            ApplicableDocumentTypes = applicableDocumentTypes.ToList(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, CustomFieldType type, IEnumerable<DocumentType> applicableDocumentTypes, bool isActive)
    {
        Name = name;
        Type = type;
        ApplicableDocumentTypes = applicableDocumentTypes.ToList();
        IsActive = isActive;
    }
}
