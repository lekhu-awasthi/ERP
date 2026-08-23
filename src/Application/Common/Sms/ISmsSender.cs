namespace ErpApp.Application.Common.Sms;

/// <summary>
/// Stubbed behind this interface per roadmap Phase 18 -- a real gateway is later hardening, not
/// required this phase (docs/phase-18-status.md decision #6). Infrastructure wires a log-to-console
/// implementation, the same "stub the external channel, build the domain logic for real" pattern
/// IEmailSender already established in Phase 1a.
/// </summary>
public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);
}
