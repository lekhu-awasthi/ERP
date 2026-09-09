namespace ErpApp.Domain.Tenancy;

/// <summary>
/// `RolePermission { RoleId, PermissionKey, LocationId, IsGranted }` per architecture-spec.md §3.7 --
/// PermissionKey is a stable string (see Application.Common.Security.PermissionKeys), evaluated
/// by AuthorizationBehavior. No scope/module/documentType/action decomposition yet (the full
/// (scope, module, documentType, action) matrix is later work); PermissionKey is just a flat
/// string for now, enough to unblock every command from Phase 2 onward having somewhere to
/// check a permission.
///
/// <para><b>Phase 32b adds <see cref="LocationId"/>, and null is the organization-wide grant.</b>
/// The live role editor is two sections -- <i>Organization-wide Permissions</i>, subtitled "Apply
/// across all billing locations", and <i>Location-specific Permissions</i>, "Scoped to individual
/// billing locations" -- and the 2026-09-09 pass proved the two are independent stores: a role
/// carrying 25 of 94 org-wide Transactions grants showed 0 of 94 at every one of the tenant's three
/// locations. So a null here means the row grants its key <b>everywhere</b>, and a non-null one
/// grants it at that location only; the effective test is org-wide <b>OR</b> location-specific, and
/// a tenant that never opens the second section behaves exactly as it did before this phase.</para>
///
/// <para><b>An Id, not a code segment.</b> architecture-spec.md §3.7 guessed the key shape
/// <c>"HeadOffice.Sales.Invoice.Approve"</c> and the live editor confirms that <i>display</i> shape,
/// but nothing in the DOM ever exposes it as storage (the chips are buttons with no key attribute).
/// A location code is renameable, so persisting it inside the key string would silently orphan every
/// grant the first time an Admin edits a code -- the identical argument
/// <see cref="BillingLocation"/> already makes for why documents store an FK rather than a code.
/// The prefixed string stays a rendering of (location, key), produced by the matrix query and never
/// written.</para>
///
/// <para><b>Nothing is ever seeded per location.</b> The roadmap flagged N locations × 94 keys as
/// "the first permission set whose row count is a function of tenant data". It is not: an absent row
/// is a denial, the live editor's own default is 0 of 94 at every location, so only <i>granted</i>
/// rows exist and a fresh tenant writes none at all. That is also why adding or deactivating a
/// location needs no migration -- see docs/phase-32b-status.md.</para>
/// </summary>
public sealed class RolePermission
{
    public Guid Id { get; private set; }
    public Guid RoleId { get; private set; }
    public string PermissionKey { get; private set; } = null!;

    /// <summary>
    /// The billing location this grant is scoped to, or <b>null for an organization-wide grant that
    /// applies at every location</b> -- see the type's own remarks. Every row written before phase
    /// 32b is null, which is why the migration needs no backfill.
    /// </summary>
    public Guid? LocationId { get; private set; }

    public bool IsGranted { get; private set; }

    private RolePermission()
    {
    }

    /// <summary>
    /// Constructs a row with an explicit, caller-supplied Id -- see Role.Create's doc comment
    /// for why HasData/tests need a stable Id rather than a freshly generated one.
    ///
    /// <para><paramref name="locationId"/> is trailing and optional so every existing caller (the
    /// whole <c>RolePermissionConfiguration</c> seed included) keeps constructing organization-wide
    /// rows unchanged.</para>
    /// </summary>
    public static RolePermission Create(
        Guid id, Guid roleId, string permissionKey, bool isGranted, Guid? locationId = null) =>
        new() { Id = id, RoleId = roleId, PermissionKey = permissionKey, IsGranted = isGranted, LocationId = locationId };

    /// <summary>
    /// Flips this row's grant, used by UpdateRolePermissionsCommandHandler's diff-and-save (Phase
    /// 14) -- an existing row's IsGranted is updated in place rather than the row being deleted
    /// and a fresh one re-added, matching this codebase's existing "an explicit IsGranted=false
    /// row rather than the row simply being absent" seed convention.
    /// </summary>
    public void SetGranted(bool isGranted) => IsGranted = isGranted;
}
