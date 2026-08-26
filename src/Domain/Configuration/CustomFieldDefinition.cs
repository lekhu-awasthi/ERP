using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// EAV field definition (architecture-spec.md §3.6): one definition can apply to any subset of
/// document types. Phase 2 built definition CRUD only; Phase 20a adds the value-write side
/// (CustomFieldValue commands/queries) and, with it, ChoiceOptions -- live-confirmed against the
/// real "+ADD NEW FIELD" form (Configurations > Custom Fields): picking the Choices type reveals
/// an "Option 1/+Add" list editor, a field this domain type never had. Only meaningful for
/// Type == Choices; empty for Text/Number/Description.
/// </summary>
public sealed class CustomFieldDefinition
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public CustomFieldType Type { get; private set; }
    public IReadOnlyList<DocumentType> ApplicableDocumentTypes { get; private set; } = new List<DocumentType>();
    public IReadOnlyList<string> ChoiceOptions { get; private set; } = new List<string>();
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CustomFieldDefinition()
    {
    }

    public static CustomFieldDefinition Create(
        Guid organizationId,
        string name,
        CustomFieldType type,
        IEnumerable<DocumentType> applicableDocumentTypes,
        IEnumerable<string> choiceOptions)
    {
        return new CustomFieldDefinition
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Type = type,
            ApplicableDocumentTypes = applicableDocumentTypes.ToList(),
            ChoiceOptions = choiceOptions.ToList(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        string name,
        CustomFieldType type,
        IEnumerable<DocumentType> applicableDocumentTypes,
        bool isActive,
        IEnumerable<string> choiceOptions)
    {
        Name = name;
        Type = type;
        ApplicableDocumentTypes = applicableDocumentTypes.ToList();
        ChoiceOptions = choiceOptions.ToList();
        IsActive = isActive;
    }
}
