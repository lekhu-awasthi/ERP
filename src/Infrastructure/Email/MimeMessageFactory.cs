using ErpApp.Application.Common.Email;
using MimeKit;

namespace ErpApp.Infrastructure.Email;

/// <summary>
/// Builds the MIME message both senders use.
///
/// <para>Shared deliberately rather than duplicated: the whole value of
/// <see cref="FileDropEmailSender"/> as a test sink is that the <c>.eml</c> it writes is the same
/// bytes SMTP would have carried. Two independent builders would make the sink prove something
/// about itself instead.</para>
/// </summary>
public static class MimeMessageFactory
{
    public static MimeMessage Build(EmailMessage message, string fromAddress)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(fromAddress));
        mime.Subject = message.Subject;

        foreach (var to in message.To)
        {
            mime.To.Add(MailboxAddress.Parse(to));
        }

        foreach (var cc in message.Cc ?? [])
        {
            mime.Cc.Add(MailboxAddress.Parse(cc));
        }

        // Bcc goes on the envelope, never merged into To -- see EmailMessage's remarks. On a
        // customer-facing invoice that distinction is a privacy leak, not a formatting preference.
        foreach (var bcc in message.Bcc ?? [])
        {
            mime.Bcc.Add(MailboxAddress.Parse(bcc));
        }

        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
        }

        var builder = new BodyBuilder();
        if (message.IsHtml)
        {
            builder.HtmlBody = message.Body;
        }
        else
        {
            builder.TextBody = message.Body;
        }

        foreach (var attachment in message.Attachments ?? [])
        {
            builder.Attachments.Add(
                attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        mime.Body = builder.ToMessageBody();
        return mime;
    }
}
