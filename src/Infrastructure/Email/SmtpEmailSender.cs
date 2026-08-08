using ErpApp.Application.Common.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ErpApp.Infrastructure.Email;

/// <summary>Real email delivery via SMTP (MailKit), replacing the Phase 1a console stub.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        // Port 465 is Gmail's implicit-TLS port (SslOnConnect) -- distinct from 587's STARTTLS,
        // which .NET's built-in SmtpClient doesn't handle correctly; MailKit does both properly.
        await client.ConnectAsync(_options.SmtpServer, _options.Port, SecureSocketOptions.SslOnConnect, cancellationToken);
        await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        logger.LogInformation("Email sent to {ToEmail}: {Subject}", toEmail, subject);
    }
}
