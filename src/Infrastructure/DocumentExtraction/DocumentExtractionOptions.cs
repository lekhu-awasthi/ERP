namespace ErpApp.Infrastructure.DocumentExtraction;

/// <summary>
/// Configuration for the AI-assisted Document inbox extraction (FR-10.3).
///
/// <para><b>Deliberately has no <c>.Validate(...).ValidateOnStart()</c> registration</b>, unlike
/// <c>Jwt</c>/<c>Email</c>/<c>Turnstile</c>. Two reasons, and the second is the load-bearing one.
/// First, extraction is optional by design: a deployment with no credential must boot and serve the
/// whole Document inbox, because manual conversion is the base feature. Second, every
/// <c>ValidateOnStart</c> option added to this codebase has silently reddened all four host-booting
/// <c>Api.IntegrationTests</c> suites in CI (see CLAUDE.md's Known Gotchas) -- adding one here for a
/// value that is genuinely allowed to be absent would have been paying that cost for nothing.</para>
///
/// <para><see cref="ApiKey"/> is a credential and belongs in <c>dotnet user-secrets</c>, never in
/// <c>appsettings.json</c>:
/// <c>dotnet user-secrets set "DocumentExtraction:ApiKey" "&lt;key&gt;" --project src/Api</c>.</para>
/// </summary>
public sealed class DocumentExtractionOptions
{
    public const string SectionName = "DocumentExtraction";

    /// <summary>Null or blank means extraction is not configured on this deployment;
    /// <c>ClaudeDocumentExtractor</c> then reports <c>Unavailable</c> rather than failing.</summary>
    public string? ApiKey { get; init; }

    /// <summary>The model that reads the scan. Named explicitly rather than defaulted by the SDK so
    /// the id a tenant is shown, the id recorded on every extraction, and the id actually called are
    /// the same string.</summary>
    public string ModelId { get; init; } = "claude-opus-5";

    /// <summary>Hard ceiling on one extraction call. A user is waiting on this synchronously, so it
    /// is short by design -- a timeout is an ordinary outcome that leaves the document convertible
    /// by hand.</summary>
    public int TimeoutSeconds { get; init; } = 90;

    /// <summary>Output cap for one extraction. A bill's worth of structured fields is small; this is
    /// sized to hold a long line-item list without ever running away.</summary>
    public int MaxTokens { get; init; } = 4096;
}
