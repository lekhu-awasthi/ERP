using ErpApp.Application.Common.Sms;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>
/// Records every send; optionally throws on the Nth call (1-based) to simulate a mid-batch
/// failure -- SendSmsCommandHandlerTests uses this to prove the "zero partial SmsLog rows, unchanged
/// ledger balance" atomicity requirement (docs/phase-18-status.md exit criteria #5).
/// </summary>
public sealed class FakeSmsSender(int? failOnCallNumber = null) : ISmsSender
{
    public List<(string PhoneNumber, string Message)> Sent { get; } = [];

    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        if (failOnCallNumber is { } n && Sent.Count + 1 == n)
        {
            throw new InvalidOperationException($"Simulated gateway failure on send #{n}.");
        }

        Sent.Add((phoneNumber, message));
        return Task.CompletedTask;
    }
}
