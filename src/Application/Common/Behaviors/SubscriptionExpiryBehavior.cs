using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Behaviors;

/// <summary>
/// Phase 31 -- the fifth pipeline behavior. Past <c>TenantSubscription.TermEndsAt</c> an
/// organization goes <b>read-only for business documents</b>: nothing new can be created, edited,
/// approved or voided until the subscription is extended.
///
/// <para><b>It gates exactly the set <see cref="ILockDateSensitive"/> and
/// <see cref="ILockDateSensitiveDocument"/> already mark</b>, and reuses those markers rather than
/// introducing a third. That set is precisely "a create, update, approve or void of a transactional
/// document" across all fifteen types -- phase 16a built and swept it, and it is the same set a
/// lock date freezes, for the same reason: it is what "the books" means. Reusing it means this
/// phase adds no sweep, and it means a marker can never drift out of sync between the two gates.</para>
///
/// <para><b>Phase 46 added a third marker, <see cref="IExpirySensitiveMasterData"/></b> (phase 31
/// carried item #6). Products, contacts, accounts, warehouses and billing locations are not
/// documents, so none of them carried a lock-date marker, so an expired tenant could build a whole
/// chart of accounts and product catalogue indefinitely and for free. Phase 31 excluded
/// "configuration edits" for a reason that only ever covered <i>fixing</i> something; setting up a
/// new business was never argued for and was allowed by the same gap.</para>
///
/// <para><b>What deliberately still works past expiry, and why.</b> Every query (so the tenant can
/// read, print and export everything it has -- "read-only" has to actually mean readable);
/// <b>settings</b> edits, so an expired tenant is not also locked out of correcting a wrong default
/// account or a wrong general setting before it renews -- which is phase 31's reason, kept intact and
/// now narrowed to what it actually argued for; and <c>SetTenantSubscriptionCommand</c>, which is the
/// one command that can lift the expiry and would otherwise be unreachable from inside the very
/// tenant that needs it. That last exclusion is not an accident of the marker set: that command
/// carries no document date and could not implement any of the three markers if it tried.</para>
///
/// <para><b>Registration order.</b> After <c>AuthorizationBehavior</c> and beside
/// <c>FeatureGateBehavior</c>, so a caller with no membership of the organization gets a 403 before
/// this ever discloses whether that organization's subscription has lapsed.</para>
///
/// <para><b>No live evidence for the exact behaviour past expiry.</b> The reference tenant's
/// Subscriptions screen shows "expire in 59 days" and no expired tenant is observable on the UAT
/// instance, so read-only-for-documents is derived from the roadmap's own wording rather than
/// confirmed -- recorded as such in docs/phase-31-status.md.</para>
/// </summary>
public sealed class SubscriptionExpiryBehavior<TRequest, TResponse>(IAppDbContext db)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not (ILockDateSensitive or ILockDateSensitiveDocument or IExpirySensitiveMasterData)
            || request is not IOrganizationScoped scoped)
        {
            return await next();
        }

        var termEndsAt = await db.TenantSubscriptions
            .AsNoTracking()
            .Where(x => x.OrganizationId == scoped.OrganizationId)
            .Select(x => (DateTimeOffset?)x.TermEndsAt)
            .SingleOrDefaultAsync(cancellationToken);

        // A tenant with no subscription row at all is not treated as expired: TenantSubscription is
        // seeded at Organization creation, so a missing row means a fixture or a partially migrated
        // tenant, and failing those closed would break far more than it protects.
        if (termEndsAt is { } endsAt && endsAt <= DateTimeOffset.UtcNow)
        {
            throw new ConflictException(
                $"This organization's subscription ended on {endsAt:yyyy-MM-dd} and it is now read-only. " +
                "Existing records can still be viewed, printed and exported; renew the subscription to " +
                "create or approve documents again.");
        }

        return await next();
    }
}
