namespace ErpApp.Domain.Configuration;

/// <summary>
/// Custom field value shape (architecture-spec.md §3.6). Also reused as CustomFieldValue's
/// ValueType discriminator, so a value row can be typed/cast at read time without a separate enum.
/// </summary>
public enum CustomFieldType
{
    Text,
    Number,
    Description,
    Choices,
}
