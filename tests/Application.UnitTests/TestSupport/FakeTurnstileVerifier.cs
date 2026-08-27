using ErpApp.Application.Common.BotProtection;

namespace ErpApp.Application.UnitTests.TestSupport;

public sealed class FakeTurnstileVerifier(bool shouldSucceed = true) : ITurnstileVerifier
{
    public Task<bool> VerifyAsync(string token, CancellationToken cancellationToken = default) =>
        Task.FromResult(shouldSucceed);
}
