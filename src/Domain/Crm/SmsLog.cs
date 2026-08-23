namespace ErpApp.Domain.Crm;

/// <summary>
/// One row per recipient per send (product-requirements.md FR-4.8's history log; also backs a
/// Contact's own "SMS History" activity sub-tab, filtered by ContactId). Content is the fully
/// resolved text actually sent (merge fields already substituted), not the template's raw
/// placeholder text -- so two recipients of the same template/batch can show genuinely different
/// Content. BatchId groups every SmsLog row written by one SendSmsCommand call, letting "Recent
/// SMS"-style views roll many per-recipient rows back up into one send-event row (matching the live
/// Tigg Overview tab's one-row-per-send display) without losing the per-recipient detail.
/// </summary>
public sealed class SmsLog
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid BatchId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid? TemplateId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Content { get; private set; } = null!;
    public string PhoneNumber { get; private set; } = null!;
    public int CreditsUsed { get; private set; }
    public Guid SentByUserId { get; private set; }
    public DateTimeOffset SentAt { get; private set; }

    private SmsLog()
    {
    }

    public static SmsLog Create(
        Guid organizationId,
        Guid batchId,
        Guid contactId,
        Guid? templateId,
        string title,
        string content,
        string phoneNumber,
        int creditsUsed,
        Guid sentByUserId)
    {
        return new SmsLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            BatchId = batchId,
            ContactId = contactId,
            TemplateId = templateId,
            Title = title,
            Content = content,
            PhoneNumber = phoneNumber,
            CreditsUsed = creditsUsed,
            SentByUserId = sentByUserId,
            SentAt = DateTimeOffset.UtcNow,
        };
    }
}
