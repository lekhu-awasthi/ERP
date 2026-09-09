using ErpApp.Domain.Common;

namespace ErpApp.Application.Common.Security;

/// <summary>
/// Phase 32b (FR-3.3) -- <b>which permission keys can carry a billing location</b>, and how a
/// (location, key) pair is rendered for a human.
///
/// <para>The live role editor is two sections, and the 2026-09-07/09 passes pinned the shape
/// exactly: <i>Organization-wide Permissions</i> holds General 20 + Transactions 94 + Settings 9 +
/// Reports 52 = 175, and <i>Location-specific Permissions</i> is the <b>Transactions group alone</b>
/// replicated per location -- 94 × N. So only transaction keys are ever location-scoped; General,
/// Settings and Reports are organization-wide, full stop. That was already the roadmap's reading;
/// what the 2026-09-09 pass corrected is the belief that
/// <c>TenantSettings.LocationWiseReportPermission</c> widens this set to include Reports. It does
/// not -- see <see cref="Locations.LocationAccessScope"/>.</para>
///
/// <para><b>The set is derived, never listed.</b> Every transaction key is
/// <c>{Module}.{DocumentType}.{Action}</c>, so a key is location-scopable exactly when its middle
/// segment names a <see cref="DocumentType"/> that is in <c>DocumentMechanisms.LocationBearing</c> --
/// the same list phase 32 sized the schema for. Two consequences worth stating: a later phase adding
/// a document type gets its location scoping for free the moment the type joins that list, and a key
/// like <c>Accounting.Account.Manage</c> is excluded automatically because
/// <see cref="DocumentType.Account"/> is a numbering pool rather than a location-bearing document.
/// The lookup is by <b>member name</b>, never by ordinal -- phase-26a's rule, and the reason a
/// segment that happens to match no member simply is not scopable rather than mapping to whatever
/// enum value sits at that position.</para>
///
/// <para>77 keys today: the 15 transactional types × View/Create/Edit/Approve/Void, plus
/// <c>Accounting.OpeningBalance.View</c>/<c>.Edit</c> (the opening-balance row forms lead with a
/// Location field live, and both opening-balance kinds ride the one OpeningBalance key pair).
/// <c>LocationScopedPermissionCatalogTests</c> pins the count and the derivation in both
/// directions.</para>
/// </summary>
public static class LocationScopedPermissions
{
    private static readonly HashSet<DocumentType> LocationBearing = [.. DocumentMechanisms.LocationBearing];

    /// <summary>
    /// Every <see cref="PermissionKeys"/> constant that can be granted per location, ordinal-sorted.
    /// The organization-wide matrix still offers all of them too -- an org-wide grant of one of these
    /// applies at every location ("Apply across all billing locations", the live section's own
    /// subtitle).
    /// </summary>
    public static IReadOnlyList<string> ScopableKeys { get; } = PermissionKeyCatalog.AllKeys
        .Where(IsScopable)
        .OrderBy(key => key, StringComparer.Ordinal)
        .ToList();

    private static readonly HashSet<string> ScopableKeySet = ScopableKeys.ToHashSet(StringComparer.Ordinal);

    /// <summary>Can this key be granted at a single location, rather than only organization-wide?</summary>
    public static bool IsLocationScopable(string permissionKey) => ScopableKeySet.Contains(permissionKey);

    /// <summary>
    /// The document type a scopable key belongs to, or null when the key is organization-wide only.
    /// Used by the enforcement seam to decide whether the tenant's own
    /// <c>LocationScopeMode</c> even puts this document type in scope.
    /// </summary>
    public static DocumentType? DocumentTypeOf(string permissionKey) =>
        TryDocumentType(permissionKey, out var documentType) ? documentType : null;

    /// <summary>
    /// The human-facing name of a location-scoped grant: <c>"HeadOffice.Sales.Invoice.Approve"</c>,
    /// architecture-spec.md §3.7's guessed shape, confirmed live as what the editor communicates.
    ///
    /// <para><b>This is a rendering, never a stored value.</b> It is what a 403 names, so the user
    /// who is refused can find the exact chip to tick, and what the matrix DTO labels a cell with.
    /// The grant itself is a <c>RolePermission</c> row carrying a <c>LocationId</c> FK -- see that
    /// type's remarks for why a renameable code must not end up inside a persisted key.</para>
    /// </summary>
    public static string Describe(string locationCode, string permissionKey) => $"{locationCode}.{permissionKey}";

    private static bool IsScopable(string permissionKey) =>
        TryDocumentType(permissionKey, out var documentType) && LocationBearing.Contains(documentType);

    private static bool TryDocumentType(string permissionKey, out DocumentType documentType)
    {
        documentType = default;

        var segments = permissionKey.Split('.');

        return segments.Length == 3
               && Enum.TryParse(segments[1], ignoreCase: false, out documentType)
               && Enum.IsDefined(documentType);
    }
}
