using ErpApp.Application.Common.Security;
using ErpApp.Application.Tenancy.Queries.GetTenantSubscription;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.SetTenantSubscription;

/// <summary>
/// Phase 31's <c>TenantSubscription</c> mutator, widened by phase 41 from "type a plan name and a
/// date" into "record the term that was sold". It remains the one command an expired organization
/// must still be able to run, so <c>SubscriptionExpiryBehavior</c>'s read-only state stays escapable
/// from inside the product.
///
/// <para><b>Deliberately not lock-date-sensitive, not feature-gated, and not metered</b> -- all three
/// gates would stop the one command that lifts the others. It carries no document date and could not
/// implement the lock-date markers if it tried.</para>
///
/// <para><b>The flaw this command has, named where it lives.</b> Its permission key is seeded to the
/// tenant's own Admin role, so the party a quota or an expiry constrains is the party that can raise
/// it. That is not an oversight in the wiring; it is the consequence of this codebase modelling
/// exactly one actor -- the tenant -- while a subscription is by nature a two-party record. Until a
/// vendor actor exists, this command records what a human was told to record. See
/// docs/phase-41-status.md's carried items for the re-entry condition.</para>
///
/// <para>Returns the same DTO the read query does, so the Subscription screen re-renders from the
/// response rather than re-fetching.</para>
/// </summary>
/// <param name="PlanId">A row from the catalogue, or <c>null</c> to record a term with no catalogue
/// plan behind it -- which is what a trial extension is. Phase 31 accepted free text here; a picker
/// replaced it, because a plan name nobody can mistype is the whole reason a catalogue exists.</param>
/// <param name="EndsAt">When the term being recorded ends.</param>
/// <param name="SubscriptionAmount">What the tenant is charged. Defaults to the plan's published
/// annual price when omitted; passed explicitly for a negotiated rate, a multi-year term or a bundle
/// including add-ons.</param>
/// <param name="ProductQuota">The product ceiling. Defaults to the plan's, and is passed explicitly
/// when add-ons were bought (Rs 1,000 per additional 1,000). <c>0</c> means not metered, which is
/// what a trial extension records.</param>
/// <param name="TransactionQuota">As <paramref name="ProductQuota"/>, for transactions per term
/// (Rs 1,000 per additional 10,000).</param>
/// <param name="DailyAiScanQuota">Phase 46 -- AI extractions per day. Defaults to the plan's, or to
/// the published 20 when there is no plan; <c>0</c> exempts the tenant entirely, which is a
/// deliberate act rather than the trial default the other two quotas have.</param>
/// <param name="LocationQuota">Phase 46 -- how many billing locations were paid for (Rs 5,000 each
/// per year). A record only: nothing refuses on it, because the reference product does not cap
/// locations. Defaults to the count already recorded, so an unrelated save cannot clear it.</param>
/// <param name="IrdVerified">Whether the IRD Billing add-on has been paid for.</param>
public sealed record SetTenantSubscriptionCommand(
    Guid OrganizationId,
    Guid? PlanId,
    DateTimeOffset EndsAt,
    decimal? SubscriptionAmount = null,
    int? ProductQuota = null,
    int? TransactionQuota = null,
    int? DailyAiScanQuota = null,
    int? LocationQuota = null,
    bool IrdVerified = false)
    : IRequest<TenantSubscriptionDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SubscriptionManage;
}
