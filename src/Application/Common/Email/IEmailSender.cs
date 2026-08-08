namespace ErpApp.Application.Common.Email;

/// <summary>
/// Stubbed behind this interface per roadmap Phase 1a task 2 -- a real provider (SMTP/SES/etc.)
/// is later hardening, not required for the working vertical slice. Infrastructure currently
/// wires a log-to-console implementation.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default);
}
