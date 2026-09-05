namespace ErpApp.Application.Common.Email;

/// <summary>
/// Stubbed behind this interface per roadmap Phase 1a task 2; Phase 18 replaced the console stub
/// with a real MailKit SMTP implementation.
///
/// <para>Phase 30 made <see cref="EmailMessage"/> the primary shape — see that type for why the
/// three-argument overload could not express a Send Email dialog's output, and why it nonetheless
/// stays, now as a default interface method. Implementations write one method; the five Phase 1a-era
/// callers keep the call they already had.</para>
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

    Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default) =>
        SendAsync(EmailMessage.PlainText(toEmail, subject, body), cancellationToken);
}
