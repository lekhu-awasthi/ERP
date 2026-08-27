using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ErpApp.Application.Common.BotProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ErpApp.Infrastructure.BotProtection;

/// <summary>Calls Cloudflare's siteverify endpoint. See docs/phase-20g-status.md.</summary>
public sealed class TurnstileVerifier(HttpClient httpClient, IOptions<TurnstileOptions> options, ILogger<TurnstileVerifier> logger)
    : ITurnstileVerifier
{
    private const string SiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    private readonly TurnstileOptions _options = options.Value;

    public async Task<bool> VerifyAsync(string token, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync(
            SiteVerifyUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = _options.SecretKey,
                ["response"] = token,
            }),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken);

        if (result is null || !result.Success)
        {
            logger.LogWarning("Turnstile verification failed: {Errors}",
                result?.ErrorCodes is { Length: > 0 } errors ? string.Join(", ", errors) : "unknown");
        }

        return result?.Success ?? false;
    }

    private sealed record SiteVerifyResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);
}
