namespace ErpApp.Application.Common.BotProtection;

/// <summary>
/// Verifies a Cloudflare Turnstile token against Cloudflare's siteverify endpoint.
/// Roadmap Phase 20g -- the Phase 1 registration hardening deferral (FR-1.1).
/// </summary>
public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string token, CancellationToken cancellationToken = default);
}
