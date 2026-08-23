namespace ErpApp.Application.Common.Storage;

/// <summary>
/// Phase 18's file-storage abstraction (docs/phase-18-status.md decision #1) -- designed once here
/// so Phase 22's Document inbox can reuse it without a rewrite. Deliberately minimal: no
/// provider-shaped parameters (no bucket/container/blob-tier concepts) leak into this interface, so
/// a future cloud implementation (S3/Blob) is a drop-in IFileStorage without touching any caller.
/// The key returned by SaveAsync is an opaque storage identifier, not a display file name or a
/// public URL -- callers persist it (Attachment.StorageKey) and pass it back to open/delete. There
/// is deliberately no "resolve to a public URL" method: every download goes through an
/// authenticated, permission-checked Api endpoint (see AttachmentsEndpoints), never a raw static
/// path, so a caller only ever needs a Stream back, not a URL a browser could hit directly.
/// </summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
