using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Generic EAV value row (architecture-spec.md §3.6), keyed by (FieldDefinitionId, DocumentType,
/// DocumentId). Value is stored as nvarchar(max) with a ValueType discriminator for query-ability
/// -- the spec's "least clever option" over JSON/sql_variant. Entity + EF mapping only in Phase 2:
/// no command/query reads or writes this yet, since no real document type exists before Phase 4+.
/// </summary>
public sealed class CustomFieldValue
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid FieldDefinitionId { get; private set; }
    public DocumentType DocumentType { get; private set; }
    public Guid DocumentId { get; private set; }
    public string Value { get; private set; } = null!;
    public CustomFieldType ValueType { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CustomFieldValue()
    {
    }

    public static CustomFieldValue Create(
        Guid organizationId,
        Guid fieldDefinitionId,
        DocumentType documentType,
        Guid documentId,
        string value,
        CustomFieldType valueType)
    {
        return new CustomFieldValue
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            FieldDefinitionId = fieldDefinitionId,
            DocumentType = documentType,
            DocumentId = documentId,
            Value = value,
            ValueType = valueType,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
