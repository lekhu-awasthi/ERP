using System.Net.Mail;
using ErpApp.Domain.Configuration;

namespace ErpApp.Application.Configuration.Commands;

/// <summary>
/// Recipient-list rules shared by the Create and Update AlertDefinition validators.
///
/// <para>Address format is validated <b>here, at definition time</b>, and deliberately not by the
/// dispatcher: the dispatcher runs with nobody watching, so a malformed address discovered there
/// can only ever become a Failed row somebody has to go looking for. Catching it on the form gives
/// the admin the error while they are still on the screen that caused it. The dispatcher still
/// tolerates a bad address (SmtpEmailSender throwing is recorded as Failed, not a crashed tick) --
/// belt and braces, since a definition could predate a validator change.</para>
///
/// <para><see cref="MailAddress"/> rather than a regex: this is the same parser SmtpClient itself
/// will use downstream, so it accepts exactly the set of addresses that can actually be sent to.
/// A hand-rolled regex would disagree with the sender in both directions.</para>
/// </summary>
public static class AlertDefinitionValidation
{
    /// <summary>Enough for roughly 30 typical addresses. Matches the column length in
    /// AlertDefinitionConfiguration -- the two must stay in step or a valid form submission fails
    /// at the database instead of at the validator.</summary>
    public const int MaxRecipientsLength = 1000;

    public static bool HasAtLeastOneRecipient(string? recipients) =>
        AlertDefinition.ParseRecipients(recipients).Count > 0;

    public static bool AllRecipientsAreValidEmails(string? recipients)
    {
        var parsed = AlertDefinition.ParseRecipients(recipients);
        return parsed.Count > 0 && parsed.All(IsValidEmail);
    }

    private static bool IsValidEmail(string address)
    {
        // MailAddress accepts a display-name form ("Name <a@b.com>"); an alert recipient is a bare
        // address, so the round-trip check rejects anything that is not exactly one.
        return MailAddress.TryCreate(address, out var parsed)
               && string.Equals(parsed.Address, address, StringComparison.OrdinalIgnoreCase);
    }
}
