using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Behaviors;

/// <summary>
/// Enforces the tenant's opted-in Accounting Features (FR-2.6) at point of use, in the one shared
/// place every feature-gated command/query flows through -- the same "one pipeline behavior, not
/// N hand-written guards" discipline AuthorizationBehavior established for permissions and
/// LockDateBehavior for period close. Until Phase 20f, TenantSubscription was written once at
/// Organization creation and read nowhere; this behavior is what makes it a real gate.
///
/// Runs after AuthorizationBehavior and before LockDateBehavior (registration order in
/// DependencyInjection.AddApplication): a caller who isn't allowed to touch this document type at
/// all gets a permission 403 first, without learning anything about the tenant's entitlements,
/// and a request blocked by an entitlement never reaches the lock-date lookup.
///
/// A request that doesn't implement <see cref="IRequireFeature"/> skips this entirely.
/// </summary>
public sealed class FeatureGateBehavior<TRequest, TResponse>(IAppDbContext db) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IRequireFeature featureRequest)
        {
            return await next();
        }

        // Deliberately a throw, not a silent skip: an IRequireFeature request with no
        // OrganizationId has no tenant whose flags could be read, so the gate would no-op
        // forever without anything failing -- exactly the failure mode phase-12 hit when
        // IOrganizationScoped requests skipped AuthorizationBehavior. Caught by the first test
        // or first call, never in production silence.
        if (request is not IOrganizationScoped scoped)
        {
            throw new InvalidOperationException(
                $"{typeof(TRequest).Name} implements IRequireFeature but not IOrganizationScoped, so its " +
                "tenant's feature flags cannot be resolved. Every feature-gated request must be organization-scoped.");
        }

        var subscription = await db.TenantSubscriptions
            .SingleOrDefaultAsync(x => x.OrganizationId == scoped.OrganizationId, cancellationToken);

        foreach (var feature in featureRequest.RequiredFeatures)
        {
            // Fail closed on a missing subscription row. Every Organization created through
            // CreateOrganizationCommand gets one in the same SaveChanges, so this only fires for
            // hand-inserted rows -- and "no entitlements recorded" must mean "no entitlements",
            // never "all entitlements".
            if (subscription is null || !subscription.IsEnabled(feature))
            {
                throw new FeatureNotEnabledException(
                    $"This organization does not have the {Describe(feature)} feature enabled. " +
                    "Accounting Features are chosen when the organization is created and cannot be changed afterwards.");
            }
        }

        return await next();
    }

    /// <summary>The wizard's own Step 2 wording for each checkbox, so the error names the feature
    /// the way the user saw it when they did (or didn't) tick it.</summary>
    private static string Describe(TenantFeature feature)
    {
        return feature switch
        {
            TenantFeature.TrackInventory => "Track Inventory",
            TenantFeature.MultipleLocations => "Multiple Locations",
            TenantFeature.MultipleWarehouses => "Multiple Warehouses",
            TenantFeature.MultiCurrency => "Multi-Currency Support",
            TenantFeature.Manufacturing => "Manufacturing",
            TenantFeature.PosRetail => "Point of Sale (Retail)",
            TenantFeature.PosRestaurant => "Point of Sale (Restaurant)",
            _ => feature.ToString(),
        };
    }
}
