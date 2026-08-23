namespace ErpApp.Application.Common.Storage;

/// <summary>
/// File validation at the storage boundary (docs/phase-18-status.md decision #1) -- max size and an
/// extension allow-list, reasonable Nepali-SME defaults (PDF, images, common Office formats).
/// Deliberately no virus/malware scanning (explicitly out of scope this phase). Fixed constants, not
/// a per-tenant configurable setting -- nothing in FR-4.5 or the live Tigg screen suggested this
/// should vary per organization.
/// </summary>
public static class AttachmentValidation
{
    public const long MaxSizeBytes = 10 * 1024 * 1024;

    public static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt",
    };

    public static bool IsAllowedExtension(string fileName) =>
        AllowedExtensions.Contains(Path.GetExtension(fileName));
}
