using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Tenant-scoped merge-field text template (architecture-spec.md line ~108; erp-module-scan.md
/// §13; FR-11.3) -- Customer/Supplier Balance Confirmation letters, Terms and Conditions, and
/// Email bodies. Body is plain text with merge-field placeholders; Phase 18's SMS Templates
/// already established a `$[placeholder]$` convention elsewhere in this codebase (see
/// SmsTemplate), reused here by documentation convention only -- no live validation enforces the
/// syntax this phase (docs/phase-20d-status.md: Step 1 couldn't re-confirm the exact syntax on
/// this specific screen, and the roadmap kickoff only asked for validation "if the real product
/// enforces one").
/// </summary>
public sealed class CustomTemplate : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public CustomTemplateType Type { get; private set; }
    public string Body { get; private set; } = null!;
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CustomTemplate()
    {
    }

    /// <summary>isDefault is set by the caller, same split as PrintingTemplate.Create.</summary>
    public static CustomTemplate Create(Guid organizationId, string name, CustomTemplateType type, string body, bool isDefault)
    {
        return new CustomTemplate
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Type = type,
            Body = body,
            IsDefault = isDefault,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Same "clear IsDefault on a Type move" invariant as PrintingTemplate.Update.</summary>
    public void Update(string name, CustomTemplateType type, string body, bool isActive)
    {
        if (Type != type)
        {
            IsDefault = false;
        }

        Name = name;
        Type = type;
        Body = body;
        IsActive = isActive;
    }

    public void MarkAsDefault() => IsDefault = true;

    public void ClearDefault() => IsDefault = false;
}
