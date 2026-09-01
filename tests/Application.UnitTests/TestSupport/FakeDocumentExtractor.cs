using ErpApp.Application.Common.DocumentExtraction;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>
/// In-memory <see cref="IDocumentExtractor"/>. <b>No test in this suite may touch the network</b> --
/// this is the seam that guarantees it, and every assertion is written against the
/// <i>contract</i> (a suggestion arrives, a failure leaves the document convertible) rather than
/// against any model's output.
///
/// <para>Four behaviours, because the four are what the phase promised to handle: a good result, a
/// vendor failure, no credential at all, and an implementation that breaks the interface's
/// never-throw contract outright.</para>
/// </summary>
public sealed class FakeDocumentExtractor : IDocumentExtractor
{
    public enum Behavior
    {
        Succeed,
        Fail,
        Unavailable,
        Throw,
    }

    public FakeDocumentExtractor(Behavior behavior = Behavior.Succeed, ExtractedDocumentData? data = null)
    {
        Configured = behavior != Behavior.Unavailable;
        _behavior = behavior;
        _data = data ?? new ExtractedDocumentData { PartyName = "Fake Supplier Pvt. Ltd." };
    }

    private readonly Behavior _behavior;
    private readonly ExtractedDocumentData _data;

    public bool Configured { get; init; }

    public int CallCount { get; private set; }

    /// <summary>The bytes the extractor was handed, so a test can assert the file really was read
    /// (and, just as importantly, that nothing else was).</summary>
    public byte[]? LastContent { get; private set; }

    public bool IsConfigured => Configured;

    public string ModelId => "fake-model-1";

    public async Task<DocumentExtractionOutcome> ExtractAsync(
        Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        CallCount++;

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        LastContent = buffer.ToArray();

        return _behavior switch
        {
            Behavior.Succeed => DocumentExtractionOutcome.Success(_data, ModelId),
            Behavior.Fail => DocumentExtractionOutcome.Failure("The extraction service could not be reached.", ModelId),
            Behavior.Unavailable => DocumentExtractionOutcome.NotConfigured(
                "AI-assisted extraction is not configured on this server."),
            Behavior.Throw => throw new HttpRequestException("simulated vendor blow-up"),
            _ => throw new ArgumentOutOfRangeException(nameof(content)),
        };
    }
}
