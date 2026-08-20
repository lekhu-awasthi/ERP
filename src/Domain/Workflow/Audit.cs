using ErpApp.Domain.Common;

namespace ErpApp.Domain.Workflow;

/// <summary>
/// Append-only audit trail row (architecture-spec.md §3.9, FR-9.6/NFR-3.3), written once by
/// AuditBehavior (Application/Common/Behaviors/AuditBehavior.cs) after a command's handler
/// completes successfully -- never updated or deleted by any code path (enforced a second way,
/// not just by this type's own missing Update/Delete methods: see AppDbContext.SaveChangesAsync's
/// override, which throws if any tracked Audit row is ever Modified/Deleted).
///
/// DocumentType/DocumentId are deliberately generic, not scoped to this phase's own System Audit
/// report -- architecture-spec.md §3.9 states this same behavior also backs the future Contact/
/// Organization/Product "Activity" tab (filtered by DocumentId alone), so the shape must support
/// querying by DocumentId with no other filter, not just by this phase's own report filters.
/// </summary>
public sealed class Audit
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public string Action { get; private set; } = null!;
    public DocumentType DocumentType { get; private set; }
    public Guid DocumentId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Audit()
    {
    }

    public static Audit Create(
        Guid organizationId, Guid userId, string action, DocumentType documentType, Guid documentId)
    {
        return new Audit
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            Action = action,
            DocumentType = documentType,
            DocumentId = documentId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
