using System.Globalization;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using ErpApp.Application.Common.DocumentExtraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ErpApp.Infrastructure.DocumentExtraction;

/// <summary>
/// The only implementation of <see cref="IDocumentExtractor"/>. Lives in Infrastructure, with every
/// vendor concern -- SDK, credential, model id, prompt, schema, timeout -- contained here, exactly as
/// <c>TurnstileVerifier</c> contains Cloudflare's. Nothing above Infrastructure knows a third party
/// exists.
///
/// <para><b>What actually leaves the tenant</b> (docs/phase-22-status.md, Decision C): the bytes of
/// the one document a user clicked Extract on, plus a fixed prompt. Nothing else -- no Contact list,
/// no Product catalogue, no organization name, no user identity, no other document. The tenant's own
/// data is used to *resolve* the answer afterwards, in this codebase, by
/// <c>GetInboxDocumentPrefillQueryHandler</c>; it is never sent outward to help the model guess.</para>
///
/// <para><b>The scan carries a supplier's PAN, address and often a signature.</b> That is inherent to
/// sending a scanned bill at all and cannot be redacted out of an image without reading it first --
/// which is the very thing being outsourced. The honest mitigations are the ones actually built:
/// the tenant must opt in (default off), only an explicitly granted user can run it, it never fires
/// automatically, and the screen says plainly what it does before the click.</para>
///
/// <para>Reads options through <see cref="IOptionsMonitor{T}"/>, not <see cref="IOptions{T}"/> --
/// the latter caches at first resolution and does not observe a later <c>dotnet user-secrets set</c>
/// (CLAUDE.md's Known Gotchas), which is exactly the trap when flipping a credential mid-session to
/// test the unconfigured path.</para>
/// </summary>
public sealed class ClaudeDocumentExtractor(
    IOptionsMonitor<DocumentExtractionOptions> options, ILogger<ClaudeDocumentExtractor> logger)
    : IDocumentExtractor
{
    /// <summary>
    /// The whole prompt. Written to make abstention the easy path: a model that guesses a supplier
    /// PAN produces a field a human may not re-read, whereas a null produces an empty box they
    /// obviously must fill. Dates are pinned to ISO explicitly for the same reason phase-21c's import
    /// reader needs an explicit format list -- 07/08/2024 is a real date under two readings, and in
    /// statutory data the wrong one is silent.
    /// </summary>
    private const string SystemPrompt = """
        You read scanned business documents for a Nepali accounting system and return structured
        fields. You are producing a suggestion that a human will review and correct before anything
        is saved; you are not producing a record.

        Rules:
        - Report only what is legibly printed on the document. If a field is absent, unclear, or you
          are not confident, return null for it. A null is always better than a guess.
        - Return dates as ISO yyyy-MM-dd. If the document shows a Bikram Sambat (BS) date only, or
          the date is ambiguous, return null rather than converting or guessing.
        - Amounts are plain numbers with no currency symbol, thousands separator, or sign.
        - partyName is the counterparty printed on the document (the supplier on a purchase bill,
          the customer on a sales invoice), never the reader's own organization.
        - partyPan is the counterparty's PAN/VAT registration number exactly as printed.
        - Do not invent line items to make a total reconcile. If the lines are unreadable, return an
          empty list and leave totalAmount as printed.
        """;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.CurrentValue.ApiKey);

    public string ModelId => options.CurrentValue.ModelId;

    public async Task<DocumentExtractionOutcome> ExtractAsync(
        Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var settings = options.CurrentValue;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return DocumentExtractionOutcome.NotConfigured(
                "AI-assisted extraction is not configured on this server. The document can still be converted by hand.");
        }

        var mediaType = ResolveMediaType(fileName, contentType);
        if (mediaType is null)
        {
            return DocumentExtractionOutcome.Failure(
                "Extraction only works on PDF, PNG, JPEG, GIF and WebP files. The document can still be converted by hand.");
        }

        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            await content.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        // The whole call is bounded, and the timeout is linked to the caller's token so a user who
        // navigates away still cancels immediately.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        try
        {
            var client = new AnthropicClient { ApiKey = settings.ApiKey };

            var response = await client.Messages.Create(
                new MessageCreateParams
                {
                    Model = settings.ModelId,
                    MaxTokens = settings.MaxTokens,
                    System = SystemPrompt,
                    OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = ResponseSchema } },
                    Messages =
                    [
                        new MessageParam
                        {
                            Role = Role.User,
                            Content = new List<ContentBlockParam>
                            {
                                BuildSourceBlock(mediaType, bytes),
                                new TextBlockParam { Text = "Extract the fields from this document." },
                            },
                        },
                    ],
                },
                cancellationToken: timeout.Token);

            var json = ReadText(response);

            if (string.IsNullOrWhiteSpace(json))
            {
                return DocumentExtractionOutcome.Failure(
                    "The extraction service returned nothing usable. The document can still be converted by hand.",
                    settings.ModelId);
            }

            var data = Parse(json);

            return data is null
                ? DocumentExtractionOutcome.Failure(
                    "The extraction service returned a response this system could not read. The document can still be converted by hand.",
                    settings.ModelId)
                : DocumentExtractionOutcome.Success(data, settings.ModelId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away, not the vendor -- let the request cancel rather than recording a
            // failure the user will never see.
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Document extraction timed out after {Seconds}s.", settings.TimeoutSeconds);
            return DocumentExtractionOutcome.Failure(
                $"Extraction timed out after {settings.TimeoutSeconds} seconds. Try again, or convert the document by hand.",
                settings.ModelId);
        }
        catch (Exception ex)
        {
            // Never surfaces the vendor's message: it can echo request content, and this string is
            // shown to a user verbatim.
            logger.LogWarning(ex, "Document extraction failed.");
            return DocumentExtractionOutcome.Failure(
                "The extraction service could not be reached. Try again later, or convert the document by hand.",
                settings.ModelId);
        }
    }

    private static ContentBlockParam BuildSourceBlock(string mediaType, byte[] bytes)
    {
        var data = Convert.ToBase64String(bytes);

        return mediaType == "application/pdf"
            ? new DocumentBlockParam { Source = new Base64PdfSource { Data = data } }
            : new ImageBlockParam { Source = new Base64ImageSource { Data = data, MediaType = mediaType } };
    }

    /// <summary>Trusts the file extension over the browser-supplied Content-Type, which is client
    /// input -- the same reason <c>InboxDocumentValidation</c> keys its allow-list off the
    /// extension.</summary>
    private static string? ResolveMediaType(string fileName, string contentType) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => contentType == "application/pdf" ? "application/pdf" : null,
        };

    private static string ReadText(Message response)
    {
        var text = string.Empty;

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var textBlock))
            {
                text += textBlock.Text;
            }
        }

        return text;
    }

    /// <summary>
    /// Parses defensively rather than binding straight onto
    /// <see cref="ExtractedDocumentData"/>: everything here is model output, so a wrong type, an
    /// unparseable date or a number written as "1,250.00" must degrade that one field to null, never
    /// throw away the whole extraction.
    /// </summary>
    private static ExtractedDocumentData? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var lines = new List<ExtractedDocumentLine>();

            if (root.TryGetProperty("lines", out var lineArray) && lineArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var line in lineArray.EnumerateArray())
                {
                    if (line.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    lines.Add(new ExtractedDocumentLine
                    {
                        Description = ReadString(line, "description"),
                        Quantity = ReadDecimal(line, "quantity"),
                        Rate = ReadDecimal(line, "rate"),
                        Amount = ReadDecimal(line, "amount"),
                    });
                }
            }

            return new ExtractedDocumentData
            {
                PartyName = ReadString(root, "partyName"),
                PartyPan = ReadString(root, "partyPan"),
                DocumentDate = ReadDate(root, "documentDate"),
                Reference = ReadString(root, "reference"),
                TotalAmount = ReadDecimal(root, "totalAmount"),
                VatAmount = ReadDecimal(root, "vatAmount"),
                Lines = lines,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : null;

    private static decimal? ReadDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        // A schema-constrained model still occasionally writes "1,250.00". Strip the separators
        // rather than dropping a total the user would then have to retype.
        return value.ValueKind == JsonValueKind.String
            && decimal.TryParse(
                value.GetString()?.Replace(",", string.Empty, StringComparison.Ordinal),
                NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    /// <summary>Exact ISO only, matching the prompt. Anything else is null on purpose -- a
    /// day-first/month-first misreading of a statutory date is silent and has nothing to reconcile it
    /// against (the phase-21c import-date lesson).</summary>
    private static DateOnly? ReadDate(JsonElement element, string name)
    {
        var text = ReadString(element, name);

        return text is not null
            && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
    }

    /// <summary>
    /// The JSON schema the response is constrained to, mirroring
    /// <see cref="ExtractedDocumentData"/> field for field. Every field is nullable and none is
    /// required, so "I could not read this" is expressible without the model having to invent a
    /// placeholder.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, JsonElement> ResponseSchema =
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "partyName":    { "type": ["string", "null"] },
                "partyPan":     { "type": ["string", "null"] },
                "documentDate": { "type": ["string", "null"], "description": "ISO yyyy-MM-dd, or null" },
                "reference":    { "type": ["string", "null"] },
                "totalAmount":  { "type": ["number", "null"] },
                "vatAmount":    { "type": ["number", "null"] },
                "lines": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": {
                      "description": { "type": ["string", "null"] },
                      "quantity":    { "type": ["number", "null"] },
                      "rate":        { "type": ["number", "null"] },
                      "amount":      { "type": ["number", "null"] }
                    }
                  }
                }
              }
            }
            """)!;
}
