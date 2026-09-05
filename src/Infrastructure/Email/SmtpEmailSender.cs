using ErpApp.Application.Common.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ErpApp.Infrastructure.Email;

/// <summary>Real email delivery via SMTP (MailKit), replacing the Phase 1a console stub. Phase 30
/// widened it to <see cref="EmailMessage"/> — multiple recipients, CC/BCC, Reply-To, attachments
/// and an HTML body — and moved the MIME construction into <see cref="MimeMessageFactory"/> so the
/// file-drop sink writes byte-identical messages.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var mime = MimeMessageFactory.Build(message, _options.From);

        using var client = new SmtpClient();
        // Port 465 is Gmail's implicit-TLS port (SslOnConnect) -- distinct from 587's STARTTLS,
        // which .NET's built-in SmtpClient doesn't handle correctly; MailKit does both properly.
        await client.ConnectAsync(_options.SmtpServer, _options.Port, SecureSocketOptions.SslOnConnect, cancellationToken);
        await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        logger.LogInformation(
            "Email sent to {RecipientCount} recipient(s) with {AttachmentCount} attachment(s): {Subject}",
            message.To.Count, message.Attachments?.Count ?? 0, message.Subject);
    }
}
