using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// A tenant's reusable outbound-email template (FR-11.1/11.3), scoped to one
/// <see cref="EmailTemplateContext"/>. Live-confirmed 2026-09-05 — see docs/phase-30-status.md
/// Step 1.1 for the editor's full field list and Decision B for why this is its own aggregate
/// rather than a <see cref="CustomTemplate"/> with six nullable columns.
///
/// <para>Implements <see cref="ITenantLookupEntity"/> so the generic ListLookupsQuery /
/// DeleteLookupCommand pair covers list and delete, exactly as <see cref="CustomTemplate"/>,
/// <see cref="PrintingTemplate"/> and <see cref="AlertDefinition"/> do; Create/Update stay concrete
/// because the extra fields diverge.</para>
///
/// <para><b><see cref="Context"/> is immutable after creation.</b> That is a live invariant, not a
/// simplification: the reference product renders its Template Type picker <i>disabled</i> on the
/// edit screen. It also has real force here — a template body is written against one context's
/// merge fields (<c>$[INVOICE_NO]$</c> has no meaning on a Purchase Order), so silently moving a
/// template between contexts would turn a working template into one that renders raw placeholders
/// into a customer's inbox. <see cref="CustomTemplate.Update"/> deliberately does allow a Type move
/// (clearing <c>IsDefault</c>); the two aggregates do not share the invariant, which is one more
/// reason they are not one aggregate.</para>
///
/// <para><b>Body is stored with merge fields unresolved</b> — <c>$[TOKEN]$</c>, the syntax
/// <c>SmsTemplate</c> established in phase 18 and the live pass confirmed here verbatim. Resolution
/// happens once, when the Send Email dialog is opened, and what the user then edits and sends is
/// the document's own text. See <c>EmailMergeFields</c> for the catalogue and
/// <c>PrepareEmailQuery</c> for where substitution happens.</para>
/// </summary>
public sealed class EmailTemplate : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>Fixed at creation — see the type-level remarks.</summary>
    public EmailTemplateContext Context { get; private set; }

    /// <summary>Subject line, merge fields unresolved. Required, matching the live `Subject *`.</summary>
    public string Subject { get; private set; } = null!;

    /// <summary>Body, merge fields unresolved. The reference product's editor is rich text; this
    /// stores whatever the client sends and the renderer treats it as HTML.</summary>
    public string Body { get; private set; } = null!;

    /// <summary>Live `Reply to *` — a template-level default the dialog pre-fills. Nullable here
    /// though the live field is starred, because the live form defaults it to the signed-in user
    /// rather than making the admin type one, and this codebase does the same defaulting at
    /// dialog-open time. A stored null therefore means "use the sender", not "invalid".</summary>
    public string? ReplyTo { get; private set; }

    /// <summary>Comma-separated default CC list, or null. Same single-string-not-child-collection
    /// reasoning as <see cref="AlertDefinition.Recipients"/>: nothing joins to, filters by or
    /// aggregates over an individual address, so a child table would buy nothing and would drag in
    /// phase 4's full-collection-replace gotcha for free.</summary>
    public string? Cc { get; private set; }

    /// <summary>Comma-separated default BCC list, or null. See <see cref="Cc"/>.</summary>
    public string? Bcc { get; private set; }

    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private EmailTemplate()
    {
    }

    /// <summary>isDefault is set by the caller, the same split
    /// <see cref="CustomTemplate.Create"/>/<see cref="PrintingTemplate"/> use.</summary>
    public static EmailTemplate Create(
        Guid organizationId,
        string name,
        EmailTemplateContext context,
        string subject,
        string body,
        string? replyTo,
        string? cc,
        string? bcc,
        bool isDefault)
    {
        return new EmailTemplate
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Context = context,
            Subject = subject,
            Body = body,
            ReplyTo = NullIfBlank(replyTo),
            Cc = NullIfBlank(cc),
            Bcc = NullIfBlank(bcc),
            IsDefault = isDefault,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>No <c>context</c> parameter, deliberately — see the type-level remarks. Because the
    /// context cannot move, <see cref="IsDefault"/> is never cleared here, which is the other half
    /// of <see cref="CustomTemplate.Update"/>'s difference from this one.</summary>
    public void Update(
        string name, string subject, string body, string? replyTo, string? cc, string? bcc, bool isActive)
    {
        Name = name;
        Subject = subject;
        Body = body;
        ReplyTo = NullIfBlank(replyTo);
        Cc = NullIfBlank(cc);
        Bcc = NullIfBlank(bcc);
        IsActive = isActive;
    }

    public void MarkAsDefault() => IsDefault = true;

    public void ClearDefault() => IsDefault = false;

    /// <summary>An empty or whitespace-only address list is stored as null, so "no CC" has exactly
    /// one representation and a query for it cannot miss half the rows.</summary>
    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
