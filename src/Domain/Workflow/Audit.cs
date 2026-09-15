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

    /// <summary>
    /// Phase 44 (35b carried item #2) -- the billing location of the document this row is about,
    /// read once at audit-write time by <c>DocumentLocationReader</c> and then frozen.
    ///
    /// <para><b>Stamped, not joined.</b> This row is an append-only fact, and phase-35b's rule for
    /// append-only facts is that filtering on a document attribute needs a stamped column -- the
    /// alternative is a seventeen-way join back, per row, on a report over a period. Stamping also
    /// makes the value <i>historically</i> honest: it records where the document was when the action
    /// happened, which is what an audit trail is for.</para>
    ///
    /// <para><b>Null is a normal value.</b> Phase 43 made Deal and WorkTask auditable and they carry
    /// no location at all; so does any document whose type sits outside the tenant's
    /// <c>LocationScopeMode</c>, and so does every row written before this phase. A reader must treat
    /// null as "no location", never as "not yet loaded".</para>
    /// </summary>
    public Guid? LocationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private Audit()
    {
    }

    public static Audit Create(
        Guid organizationId, Guid userId, string action, DocumentType documentType, Guid documentId,
        Guid? locationId = null)
    {
        return new Audit
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            Action = action,
            DocumentType = documentType,
            DocumentId = documentId,
            LocationId = locationId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
