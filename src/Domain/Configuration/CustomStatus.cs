using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Tenant-scoped named status list, scoped to a single DocumentType (architecture-spec.md §4.10 --
/// "CustomStatus (per document type)"). Referenced by transactional documents from Phase 4+ (not
/// yet). The DocumentType discriminator is added now even though no real document-type aggregates
/// exist yet, since architecture-spec.md §3.6 already names DocumentType as the shared vocabulary
/// every later phase's aggregates will carry.
/// </summary>
public sealed class CustomStatus : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public DocumentType DocumentType { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CustomStatus()
    {
    }

    public static CustomStatus Create(Guid organizationId, string name, DocumentType documentType)
    {
        return new CustomStatus
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            DocumentType = documentType,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, DocumentType documentType, bool isActive)
    {
        Name = name;
        DocumentType = documentType;
        IsActive = isActive;
    }
}
