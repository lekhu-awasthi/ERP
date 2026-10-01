using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos;

/// <summary>
/// Phase 61 -- the accounts a till's own legs post to, resolved <b>location → tenant default → 409
/// naming the missing one</b>, and only for a leg that is actually being posted (phase 60 Decision F:
/// a tenant that never charges a service charge is never asked for its account).
///
/// <para>The location's account comes from <c>PosLocationSettings</c>; a location that has never
/// saved its settings has no row and falls straight through to the tenant default, which is the same
/// answer <c>CreateDefault</c> would have given.</para>
/// </summary>
internal static class PosAccountResolver
{
    public static async Task<Guid> ServiceChargeAccountAsync(
        IAppDbContext db, Guid organizationId, Guid? locationId, CancellationToken cancellationToken)
    {
        var atLocation = locationId is { } id
            ? await db.PosLocationSettings
                .Where(x => x.OrganizationId == organizationId && x.BillingLocationId == id)
                .Select(x => x.ServiceChargeAccountId)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        return atLocation
            ?? await TenantDefaultAsync(db, organizationId, x => x.DefaultServiceChargeAccountId, cancellationToken)
            ?? throw new ConflictException(
                "This sale carries a service charge and no account is set for it. Choose a Service Charge "
                + "account in the location's Point of Sale settings, or a Default Service Charge account under "
                + "Accounting Defaults.");
    }

    public static async Task<Guid> RoundingAccountAsync(
        IAppDbContext db, Guid organizationId, Guid? locationId, CancellationToken cancellationToken)
    {
        var atLocation = locationId is { } id
            ? await db.PosLocationSettings
                .Where(x => x.OrganizationId == organizationId && x.BillingLocationId == id)
                .Select(x => x.RoundOffAccountId)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        return atLocation
            ?? await TenantDefaultAsync(db, organizationId, x => x.DefaultRoundingAccountId, cancellationToken)
            ?? throw new ConflictException(
                "This sale is rounded and no account is set for the round-off. Choose a Round Off account in "
                + "the location's Point of Sale settings, or a Default Rounding account under Accounting Defaults.");
    }

    /// <summary>Tenant-level only: a shortage is a loss of the business, not of one branch's
    /// configuration (phase 60).</summary>
    public static async Task<Guid> CashOverShortAccountAsync(
        IAppDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
        await TenantDefaultAsync(db, organizationId, x => x.DefaultCashOverShortAccountId, cancellationToken)
        ?? throw new ConflictException(
            "The drawer's count differs from what it should hold, and no Cash Over/Short account is set to "
            + "post the difference to. Choose a Default Cash Over/Short account under Accounting Defaults.");

    private static Task<Guid?> TenantDefaultAsync(
        IAppDbContext db,
        Guid organizationId,
        System.Linq.Expressions.Expression<Func<Domain.Tenancy.TenantSettings, Guid?>> account,
        CancellationToken cancellationToken) =>
        db.TenantSettings
            .Where(x => x.OrganizationId == organizationId)
            .Select(account)
            .SingleOrDefaultAsync(cancellationToken);
}
