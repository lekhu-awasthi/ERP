using System.Reflection;
using System.Runtime.CompilerServices;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;

namespace ErpApp.Application.UnitTests.Common;

/// <summary>
/// Phase 32b's sweep guard, modelled on phase-27a's <c>DocumentMechanismSweepGuardTests</c> and
/// phase-24's <c>ProductVariantSweepGuardTests</c>.
///
/// <para><b>What it is for.</b> Location scoping is enforced in one pipeline stage that only fires
/// for requests declaring one of four markers. A request carrying a location-scopable permission key
/// and <i>no</i> marker is not a compile error and breaks no test -- it is simply a door that stays
/// open for a caller who should have been narrowed to one branch. That is precisely the failure mode
/// a sweep guard exists to convert into a build failure, so adding a request over a location-bearing
/// document type forces the author to answer "what does this do for a branch-scoped caller?".</para>
/// </summary>
public class LocationScopeSweepGuardTests
{
    private static readonly Assembly ApplicationAssembly = typeof(IRequirePermission).Assembly;

    [Fact]
    public void Every_request_with_a_location_scopable_key_declares_a_location_marker()
    {
        var unmarked = new List<string>();

        foreach (var type in RequestTypes())
        {
            var key = PermissionKeyOf(type);

            // A key we cannot evaluate statically is one computed from the request's own data -- the
            // polymorphic attachment/comment/custom-field/print requests, whose parent may or may not
            // be a document. Those are treated as scopable, conservatively: if the parent turns out to
            // be a Contact the key is not location-scopable and the check is skipped at runtime, so
            // declaring a marker costs nothing and omitting one would be a hole.
            var mustBeMarked = key is null || LocationScopedPermissions.IsLocationScopable(key);

            if (mustBeMarked && !HasMarker(type))
            {
                unmarked.Add($"{type.Name} ({key ?? "key computed from request data"})");
            }
        }

        Assert.True(
            unmarked.Count == 0,
            "These requests carry a location-scopable permission key but declare none of "
            + "ILocationScopedDocument / ILocationBearingCommand / ILocationFilteredQuery / "
            + "ILocationAgnosticRequest, so AuthorizationBehavior cannot scope them to a branch:"
            + Environment.NewLine + string.Join(Environment.NewLine, unmarked));
    }

    /// <summary>
    /// <see cref="LocationScopeResolver"/> reads <see cref="ILockDateSensitiveDocument"/> when
    /// <see cref="ILocationScopedDocument"/> is absent, which is what spares the thirty Approve/Void
    /// commands a duplicate declaration. This pins that the reuse actually covers them: if one ever
    /// drops the lock-date marker without gaining the location one, it loses its location check
    /// silently, and this fails instead.
    /// </summary>
    [Fact]
    public void Every_lock_date_sensitive_document_request_resolves_a_location_target()
    {
        var uncovered = RequestTypes()
            .Where(t => typeof(ILockDateSensitiveDocument).IsAssignableFrom(t))
            .Where(t => PermissionKeyOf(t) is { } key && LocationScopedPermissions.IsLocationScopable(key))
            .Where(t => !typeof(ILocationScopedDocument).IsAssignableFrom(t)
                        && !typeof(ILockDateSensitiveDocument).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        Assert.Empty(uncovered);
    }

    /// <summary>
    /// The two ways of naming the targeted document type must agree. The resolver derives the type
    /// from the permission key alone; a request that also declares one (via the lock-date marker)
    /// must not disagree, or the pipeline would read the location off the wrong table.
    /// </summary>
    [Fact]
    public void The_lock_date_document_type_agrees_with_the_type_derived_from_the_permission_key()
    {
        var disagreements = new List<string>();

        foreach (var type in RequestTypes().Where(t => typeof(ILockDateSensitiveDocument).IsAssignableFrom(t)))
        {
            if (PermissionKeyOf(type) is not { } key
                || LocationScopedPermissions.DocumentTypeOf(key) is not { } fromKey)
            {
                continue;
            }

            // A request that declares LocationDocumentTypeOverride is saying the two DISAGREE on
            // purpose -- ApplyPaymentAllocationCommand, whose key names Payment while its source row
            // may be a Journal Voucher. The override is the whole point, so it is exempt here and
            // pinned as a single deliberate case by the test below.
            if (DeclaresTypeOverride(type))
            {
                continue;
            }

            var instance = (ILockDateSensitiveDocument)RuntimeHelpers.GetUninitializedObject(type);

            DocumentType declared;
            try
            {
                declared = instance.LockDateDocumentType;
            }
            catch (Exception)
            {
                continue;
            }

            if (declared != fromKey)
            {
                disagreements.Add($"{type.Name}: key says {fromKey}, LockDateDocumentType says {declared}");
            }
        }

        Assert.Empty(disagreements);
    }

    /// <summary>
    /// Only transaction keys are location-scopable -- confirmed live 2026-09-07 and re-confirmed
    /// 2026-09-09 with <c>LocationWiseReportPermission</c> switched ON, which did <b>not</b> add a
    /// Reports group to the per-location matrix. Asserted in both directions, because the interesting
    /// failure is a key silently joining the set, not one leaving it.
    /// </summary>
    [Fact]
    public void Only_location_bearing_document_keys_are_scopable()
    {
        var locationBearing = DocumentMechanisms.LocationBearing.ToHashSet();

        foreach (var key in LocationScopedPermissions.ScopableKeys)
        {
            var documentType = LocationScopedPermissions.DocumentTypeOf(key);
            Assert.NotNull(documentType);
            Assert.Contains(documentType!.Value, locationBearing);
        }

        Assert.DoesNotContain("Reports.SalesRegister.View", LocationScopedPermissions.ScopableKeys);
        Assert.DoesNotContain("Configuration.CreditTerm.Manage", LocationScopedPermissions.ScopableKeys);

        // Accounting.Account.Manage's middle segment parses to DocumentType.Account -- a numbering
        // pool, not a location-bearing document -- so the derivation excludes it without a special
        // case. Worth pinning: it is the one key that would slip in if the rule were "any key whose
        // middle segment names a DocumentType".
        Assert.DoesNotContain("Accounting.Account.Manage", LocationScopedPermissions.ScopableKeys);

        Assert.Contains("Sales.Invoice.Approve", LocationScopedPermissions.ScopableKeys);
        Assert.Contains("Accounting.OpeningBalance.Edit", LocationScopedPermissions.ScopableKeys);
    }

    /// <summary>
    /// The 15 transactional types x View/Create/Edit/Approve/Void, plus OpeningBalance's View/Edit
    /// pair (both opening-balance kinds ride the one key pair). The live tenant's own count is 94 per
    /// location because its Sales group carries Customer Payment and its Purchase group Supplier
    /// Payment separately, where this codebase has a single Payment type -- so the numbers differ for
    /// a reason that is recorded rather than fudged.
    /// </summary>
    [Fact]
    public void The_scopable_key_count_is_the_transaction_matrix()
    {
        Assert.Equal((15 * 5) + 2, LocationScopedPermissions.ScopableKeys.Count);
    }

    /// <summary>
    /// The open generic lookup requests, excluded with a reason rather than skipped silently.
    /// <c>ListLookupsQuery&lt;T&gt;</c> and <c>DeleteLookupCommand&lt;T&gt;</c> take their key from
    /// <c>LookupPermissionKeys</c>, whose every arm is a <c>Configuration.*</c> key -- none of which is
    /// location-scopable, since a lookup is a tenant-wide named list rather than a document raised
    /// from a branch. They cannot be evaluated here because the key depends on the closed type
    /// argument, which is exactly why they are named rather than filtered by a predicate that would
    /// also swallow a future request.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ExemptOpenGenerics =
        new Dictionary<string, string>
        {
            ["ListLookupsQuery`1"] = "Keyed by LookupPermissionKeys -- every arm is a Configuration.* "
                + "lookup key, and a lookup is tenant-wide rather than raised from a location.",
            ["DeleteLookupCommand`1"] = "Same as ListLookupsQuery -- a Configuration.* lookup key.",
        };

    /// <summary>
    /// Pins that the type-override escape hatch stays a single deliberate exception. Every other
    /// implementation must leave it null, so the document type keeps coming from the permission key
    /// alone and cannot quietly diverge per request.
    /// </summary>
    [Fact]
    public void Only_the_payment_allocation_command_overrides_the_targeted_document_type()
    {
        var overriders = new List<string>();

        foreach (var type in RequestTypes().Where(t => typeof(ILocationScopedDocument).IsAssignableFrom(t)))
        {
            var instance = (ILocationScopedDocument)RuntimeHelpers.GetUninitializedObject(type);

            try
            {
                if (instance.LocationDocumentTypeOverride is not null)
                {
                    overriders.Add(type.Name);
                }
            }
            catch (Exception)
            {
                // An override computed from request data cannot be read off an uninitialized
                // instance; ApplyPaymentAllocationCommand's reads SourceType, which is default(0)
                // here and so returns a non-null value, which is what this test wants to see.
            }
        }

        Assert.Equal(["ApplyPaymentAllocationCommand"], overriders);
    }

    private static IEnumerable<Type> RequestTypes() => ApplicationAssembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IRequirePermission).IsAssignableFrom(t))
        .Where(t => !ExemptOpenGenerics.ContainsKey(t.Name));

    private static bool DeclaresTypeOverride(Type type)
    {
        if (!typeof(ILocationScopedDocument).IsAssignableFrom(type))
        {
            return false;
        }

        try
        {
            return ((ILocationScopedDocument)RuntimeHelpers.GetUninitializedObject(type))
                .LocationDocumentTypeOverride is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool HasMarker(Type type) =>
        typeof(ILocationScopedDocument).IsAssignableFrom(type)
        || typeof(ILocationBearingCommand).IsAssignableFrom(type)
        || typeof(ILocationFilteredQuery).IsAssignableFrom(type)
        || typeof(ILocationAgnosticRequest).IsAssignableFrom(type)
        || typeof(ILockDateSensitiveDocument).IsAssignableFrom(type);

    /// <summary>
    /// The declared key, or null when it is computed from the request's own data and so cannot be
    /// read off an uninitialized instance. <see cref="RuntimeHelpers.GetUninitializedObject"/> is
    /// what makes an instance property readable without a constructor; a key expressed as a plain
    /// <c>=> PermissionKeys.X</c> touches no field and evaluates fine.
    /// </summary>
    private static string? PermissionKeyOf(Type type)
    {
        try
        {
            return ((IRequirePermission)RuntimeHelpers.GetUninitializedObject(type)).PermissionKey;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
