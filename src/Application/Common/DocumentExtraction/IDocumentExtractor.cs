namespace ErpApp.Application.Common.DocumentExtraction;

/// <summary>
/// The AI seam (FR-10.3's stretch half). Modelled on <c>ITurnstileVerifier</c>, this codebase's
/// only other external-service abstraction: an Application-layer interface with the vendor detail
/// -- SDK, HTTP, model id, prompt, credential -- entirely inside Infrastructure, so nothing above
/// Infrastructure knows a third party exists.
///
/// <para><b>This method never throws for a vendor problem.</b> A timeout, a 429, a 500, a malformed
/// response and a missing credential are all ordinary outcomes returned as
/// <see cref="DocumentExtractionOutcome"/>, because extraction failing must leave the document
/// exactly as convertible by hand as it was a second earlier. Only a caller bug (a null stream)
/// should ever surface as an exception.</para>
/// </summary>
public interface IDocumentExtractor
{
    /// <summary>True when this deployment has a working extraction credential configured. Read by
    /// the extract command to distinguish "nobody set this up" from "the vendor failed", and by the
    /// inbox screen to decide whether to offer the button at all.</summary>
    bool IsConfigured { get; }

    /// <summary>The model this deployment would use, shown to an Admin beside the tenant's own
    /// consent switch so "what exactly reads my documents?" has an answer on screen. Exposed
    /// through the interface rather than hard-coded above Infrastructure, which is where the vendor
    /// detail belongs.</summary>
    string ModelId { get; }

    /// <summary>
    /// Reads <paramref name="content"/> and returns a suggestion, or a stated reason it could not.
    /// The stream is fully consumed before any network call, so the caller may dispose it on
    /// return.
    /// </summary>
    Task<DocumentExtractionOutcome> ExtractAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The result of one extraction attempt. <paramref name="Data"/> is non-null only when
/// <paramref name="Succeeded"/>; <paramref name="FailureReason"/> is non-null otherwise and is
/// written to be shown to a user as-is, so it must never carry a stack trace, a URL or a
/// credential.
/// </summary>
/// <param name="Succeeded">Whether a usable suggestion came back.</param>
/// <param name="Unavailable">
/// True when the attempt could not be made at all (no credential configured). Distinct from a plain
/// failure so the screen can say "ask an Admin to turn this on" rather than "try again" -- the two
/// have completely different remedies.
/// </param>
/// <param name="ModelId">
/// The exact model that produced <paramref name="Data"/>, recorded on the document so a later
/// reader can tell what guessed at a number a human then approved. Null when nothing ran.
/// </param>
public sealed record DocumentExtractionOutcome(
    bool Succeeded,
    bool Unavailable,
    ExtractedDocumentData? Data,
    string? ModelId,
    string? FailureReason)
{
    public static DocumentExtractionOutcome Success(ExtractedDocumentData data, string modelId) =>
        new(true, false, data, modelId, null);

    public static DocumentExtractionOutcome Failure(string reason, string? modelId = null) =>
        new(false, false, null, modelId, reason);

    public static DocumentExtractionOutcome NotConfigured(string reason) =>
        new(false, true, null, null, reason);
}
