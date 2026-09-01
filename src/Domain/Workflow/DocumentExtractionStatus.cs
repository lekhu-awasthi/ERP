namespace ErpApp.Domain.Workflow;

/// <summary>
/// The outcome of the optional AI-assisted field extraction (FR-10.3's "AI-assisted extraction
/// that pre-fills the transaction's fields"). Deliberately records <see cref="Failed"/> and
/// <see cref="Unavailable"/> as *ordinary, expected* outcomes rather than errors: extraction is a
/// suggestion service, and a document whose extraction never ran, timed out, or came back garbage
/// is still fully convertible by hand. Nothing in the conversion flow reads this field to decide
/// whether a conversion may proceed -- only to decide what the screen tells the user. See
/// docs/phase-22-status.md, Decision C.
/// </summary>
public enum DocumentExtractionStatus
{
    /// <summary>Never attempted. Every uploaded document starts here; extraction is an explicit,
    /// separate user action, never a side effect of upload.</summary>
    NotAttempted,

    /// <summary>The extractor returned a parsed suggestion, stored in
    /// <see cref="UploadedDocument.ExtractedDataJson"/>. Says nothing about whether the suggestion
    /// is *correct* -- a human confirms every value on the target form.</summary>
    Succeeded,

    /// <summary>Attempted and did not produce a usable suggestion (vendor error, timeout, rate
    /// limit, unparseable response). <see cref="UploadedDocument.ExtractionFailureReason"/> carries
    /// a message fit to show a user.</summary>
    Failed,

    /// <summary>Extraction could not be attempted at all -- the tenant has not opted in, or the
    /// deployment has no extraction credential configured. Distinct from <see cref="Failed"/> so
    /// the screen can say "turn this on" rather than "try again".</summary>
    Unavailable,
}
