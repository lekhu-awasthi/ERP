namespace ErpApp.Domain.Common;

/// <summary>
/// Shared shape for Configuration's tenant-scoped named lists (CreditTerm, PaymentMode,
/// CustomStatus, ReportingTagCategory, ReportingTagOption -- architecture-spec.md §4.10). An
/// interface, not a base class, matching this codebase's existing "plain sealed class, no
/// Entity/AggregateRoot base" convention (see Organization/Role/RolePermission) and its existing
/// capability-interface idiom (IRequirePermission, IOrganizationScoped). Lets the generic
/// ListLookupsQuery&lt;TLookup&gt;/DeleteLookupCommand&lt;TLookup&gt; (Application.Configuration)
/// operate on any lookup type without needing type-specific field knowledge -- Create/Update stay
/// concrete per lookup type since their extra fields genuinely diverge (see phase-2-status.md's
/// scope decisions).
/// </summary>
public interface ITenantLookupEntity
{
    Guid Id { get; }
    Guid OrganizationId { get; }
    string Name { get; }
    bool IsActive { get; }
    DateTimeOffset CreatedAt { get; }
}
