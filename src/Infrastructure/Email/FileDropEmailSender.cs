using System.Globalization;
using ErpApp.Application.Common.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ErpApp.Infrastructure.Email;

/// <summary>
/// Writes each message to disk as a real <c>.eml</c> file instead of connecting to SMTP.
///
/// <para>Phase 30 added this because the phase's exit bar requires proving a send end to end
/// <b>without mailing a real person</b>, and the two alternatives were both worse: pointing the
/// SMTP sender at a live mailbox risks exactly the accident the rule exists to prevent, and asserting
/// against a mock proves only that the mock was called. A dropped <c>.eml</c> is the genuine MIME
/// output — headers, multipart structure, attachment bytes — so it verifies the parts an
/// integration is most likely to get wrong (a BCC leaking into To, an attachment that never made
/// it), and any mail client will open one.</para>
///
/// <para>Selected by configuration (<c>Email:DeliveryMode = FileDrop</c>), never by environment
/// name: a developer debugging a production-mode issue locally must not be able to send real mail by
/// accident, and an operator must not be able to silence production mail by setting
/// <c>ASPNETCORE_ENVIRONMENT</c>. The mode is logged at startup for the same reason.</para>
/// </summary>
public sealed class FileDropEmailSender : IEmailSender
{
    private readonly string _directory;
    private readonly ILogger<FileDropEmailSender> _logger;

    public FileDropEmailSender(IOptions<EmailOptions> options, ILogger<FileDropEmailSender> logger)
    {
        _logger = logger;
        _directory = string.IsNullOrWhiteSpace(options.Value.FileDropPath)
            ? Path.Combine(Path.GetTempPath(), "erpapp-maildrop")
            : options.Value.FileDropPath;

        Directory.CreateDirectory(_directory);
        _logger.LogWarning(
            "Email delivery mode is FileDrop -- no mail will be sent. Messages are written to {Directory}.",
            _directory);
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var mime = MimeMessageFactory.Build(message, "no-reply@erpapp.local");

        // A sortable, unique, filesystem-safe name: an E2E asserting on "the newest file" gets a
        // deterministic answer even when two sends land in the same second.
        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.eml");

        var path = Path.Combine(_directory, name);

        await using (var stream = File.Create(path))
        {
            await mime.WriteToAsync(stream, cancellationToken);
        }

        _logger.LogInformation(
            "Email written to {Path} ({RecipientCount} recipient(s), {AttachmentCount} attachment(s)): {Subject}",
            path, message.To.Count, message.Attachments?.Count ?? 0, message.Subject);
    }
}
