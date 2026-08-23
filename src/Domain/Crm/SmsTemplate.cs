namespace ErpApp.Domain.Crm;

/// <summary>
/// Reusable SMS template (product-requirements.md FR-4.8). Merge-field syntax confirmed live
/// against the Tigg reference product's own Templates screen: "$[name]$", "$[balance]$",
/// "$[balance_date]$" -- resolved per-recipient by SendSmsCommandHandler against Content's raw
/// text; no separate structured field list is stored, matching the live screen's own plain
/// free-text SMS Content box with an inline merge-tag hint. Unlike Tigg's own limitation ("Merge
/// tags will only work when sending SMS from the contact detail page"), this codebase resolves
/// merge fields for every recipient in every send, including bulk sends -- a deliberate improvement
/// recorded in docs/phase-18-status.md decision #6, not a scope guess.
/// </summary>
public sealed class SmsTemplate
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Content { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private SmsTemplate()
    {
    }

    public static SmsTemplate Create(Guid organizationId, string title, string content)
    {
        return new SmsTemplate
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Title = title,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string title, string content)
    {
        Title = title;
        Content = content;
    }
}
