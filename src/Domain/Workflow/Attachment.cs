namespace ErpApp.Domain.Workflow;

/// <summary>
/// Polymorphic file attachment (architecture-spec.md §4.9-style ParentType/ParentId pattern, same
/// mechanism as WorkTask -- see AttachmentParentType's own doc comment for why it's a separate enum
/// rather than a reuse of TaskParentType). StorageKey is IFileStorage's opaque key, never a public
/// URL -- every download goes through AttachmentsEndpoints' permission-checked stream, never a raw
/// static path (docs/phase-18-status.md decision #1). No Update method -- the live Tigg Documents
/// tab only ever showed upload/delete, never an in-place file replace or metadata edit.
/// </summary>
public sealed class Attachment
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public AttachmentParentType ParentType { get; private set; }
    public Guid ParentId { get; private set; }
    public string FileName { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public string ContentType { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public Guid UploadedByUserId { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    private Attachment()
    {
    }

    public static Attachment Create(
        Guid organizationId,
        AttachmentParentType parentType,
        Guid parentId,
        string fileName,
        long sizeBytes,
        string contentType,
        string storageKey,
        Guid uploadedByUserId)
    {
        return new Attachment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ParentType = parentType,
            ParentId = parentId,
            FileName = fileName,
            SizeBytes = sizeBytes,
            ContentType = contentType,
            StorageKey = storageKey,
            UploadedByUserId = uploadedByUserId,
            UploadedAt = DateTimeOffset.UtcNow,
        };
    }
}
