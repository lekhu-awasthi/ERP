using ErpApp.Application.Common.Email;

namespace ErpApp.Application.UnitTests.TestSupport;

public sealed class FakeEmailSender : IEmailSender
{
    /// <summary>Kept as the flat tuple the Phase 1a-era tests assert on -- the To column is the
    /// first recipient, which is all any of those tests ever sends.</summary>
    public List<(string To, string Subject, string Body)> SentEmails { get; } = [];

    /// <summary>Phase 30 -- the whole message, for the tests that care about CC/BCC, Reply-To and
    /// attachments.</summary>
    public List<EmailMessage> SentMessages { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        SentMessages.Add(message);
        SentEmails.Add((message.To.FirstOrDefault() ?? string.Empty, message.Subject, message.Body));
        return Task.CompletedTask;
    }
}
