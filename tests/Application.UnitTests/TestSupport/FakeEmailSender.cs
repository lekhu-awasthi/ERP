using ErpApp.Application.Common.Email;

namespace ErpApp.Application.UnitTests.TestSupport;

public sealed class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Body)> SentEmails { get; } = [];

    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        SentEmails.Add((toEmail, subject, body));
        return Task.CompletedTask;
    }
}
