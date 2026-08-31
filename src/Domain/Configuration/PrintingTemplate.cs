using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Tenant-scoped named print-layout entry per DocumentType (architecture-spec.md line ~29;
/// erp-module-scan.md Configurations §12; FR-11.2). Phase 20d's confirm-live pass against the
/// real Tigg tenant found the reference product's "Printing Templates" screen is a genuine
/// visual template-authoring surface (a toggle/canvas editor for placing Custom Fields/
/// Organization/Date-System fields), not a fixed catalog picker -- building that editor was
/// judged out of scope for this sub-phase (see docs/phase-20d-status.md's scope decision). This
/// entity is deliberately metadata-only: Name + one IsDefault flag per (OrganizationId,
/// DocumentType), with NO layout-definition field at all. The actual print/PDF rendering (see
/// Application.Printing.Queries.PrintDocument) uses one shared, hardcoded layout per document
/// "family" (line-item vs. ledger) regardless of which row here is marked default -- this record
/// exists to satisfy FR-11.2's literal text ("a library... selecting one as the tenant's
/// default") and to prove the mechanism end-to-end, not to drive real visual differentiation.
/// That's deferred to a future phase, same as PrintProfileId was deliberately dropped rather
/// than stubbed in Phase 3 (docs/phase-3-status.md decision #1) -- see this phase's own decision
/// on why PrintProfileId stays out of scope.
/// </summary>
public sealed class PrintingTemplate : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public DocumentType DocumentType { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PrintingTemplate()
    {
    }

    /// <summary>isDefault is set by the caller (CreatePrintingTemplateCommandHandler), not decided
    /// here -- "is this the first template for this DocumentType" is a DB read, an Application-layer
    /// concern, same split CreateCostTerm/CreateCustomStatus use for their own uniqueness checks.</summary>
    public static PrintingTemplate Create(Guid organizationId, string name, DocumentType documentType, bool isDefault)
    {
        return new PrintingTemplate
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            DocumentType = documentType,
            IsDefault = isDefault,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Moving a template to a different DocumentType while it's the current default would
    /// leave two groups' default invariant inconsistent (its old group loses a default, its new
    /// group might gain a second one) -- clearing IsDefault as a side effect keeps that invariant
    /// enforceable without a separate error path for what's a rare edit.</summary>
    public void Update(string name, DocumentType documentType, bool isActive)
    {
        if (DocumentType != documentType)
        {
            IsDefault = false;
        }

        Name = name;
        DocumentType = documentType;
        IsActive = isActive;
    }

    public void MarkAsDefault() => IsDefault = true;

    public void ClearDefault() => IsDefault = false;
}
