using ErpApp.Application.Common.Security;
using ErpApp.Application.Tenancy.Queries.GetTenantSubscription;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.SetTenantSubscription;

/// <summary>
/// Phase 31 -- the <c>TenantSubscription</c> mutator phase 20f named and left out, so that
/// <c>SubscriptionExpiryBehavior</c>'s read-only state is escapable from inside the product.
///
/// <para><b>Deliberately not lock-date-sensitive and deliberately not feature-gated</b>: it is the
/// one command an expired organization must still be able to run, and both of those gates would
/// stop it. See that behavior's doc comment.</para>
///
/// <para>Returns the same DTO the read query does, so the Subscription screen re-renders from the
/// response rather than re-fetching.</para>
/// </summary>
public sealed record SetTenantSubscriptionCommand(Guid OrganizationId, string PlanName, DateTimeOffset EndsAt)
    : IRequest<TenantSubscriptionDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SubscriptionManage;
}
