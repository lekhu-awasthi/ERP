namespace ErpApp.Application.Common.Storage;

/// <summary>
/// Phase 22 (FR-10.3). <b>Reuses <see cref="AttachmentValidation"/> wholesale rather than forking
/// it</b> -- same 10 MB cap, same extension allow-list, same explicit "no virus/malware scanning"
/// scope note (docs/phase-22-status.md, Decision G).
///
/// <para>The tempting narrowing is to restrict the inbox to images and PDFs, since those are the
/// only things extraction can read. That would be the wrong cut: the inbox's base feature is
/// <i>manual</i> conversion, which works for anything a human can open, and a Nepali SME whose
/// supplier emails a .xlsx bill would otherwise have nowhere to put it. Extraction is the optional
/// half, so its narrower needs belong on <see cref="IsExtractable"/>, not on what may be
/// uploaded.</para>
/// </summary>
public static class InboxDocumentValidation
{
    public const long MaxSizeBytes = AttachmentValidation.MaxSizeBytes;

    public static bool IsAllowedExtension(string fileName) => AttachmentValidation.IsAllowedExtension(fileName);

    /// <summary>
    /// The subset an extractor can actually read: images and PDFs. Used to decide whether to offer
    /// the Extract action on a row, never to reject an upload -- a spreadsheet in the inbox is a
    /// perfectly good manually-convertible source document, it just has nothing to extract from.
    /// </summary>
    public static readonly IReadOnlySet<string> ExtractableExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".png", ".jpg", ".jpeg", ".gif" };

    public static bool IsExtractable(string fileName) =>
        ExtractableExtensions.Contains(Path.GetExtension(fileName));
}
