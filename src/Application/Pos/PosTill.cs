using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos;

/// <summary>A location that runs a till, with the settings the till reads (saved, or the defaults an
/// unsaved location reads as -- phase 60 Decision F).</summary>
internal sealed record PosTillContext(BillingLocation Location, PosLocationSettings Settings);

/// <summary>
/// Phase 61 -- what every request acting at a till checks first: the location exists, is active,
/// runs a till, and the tenant still holds that till's entitlement.
/// </summary>
internal static class PosTill
{
    /// <summary>The entitlement a mode needs. Shared with <c>SetLocationPosModeCommandHandler</c>,
    /// which asks the same question of a value it is about to set.</summary>
    public static async Task EnsureEntitledAsync(
        IAppDbContext db, Guid organizationId, PosMode mode, CancellationToken cancellationToken)
    {
        if (mode == PosMode.None)
        {
            return;
        }

        var feature = mode == PosMode.Retail ? TenantFeature.PosRetail : TenantFeature.PosRestaurant;

        // Fails closed on a missing subscription row, like FeatureGateBehavior.
        var subscription = await db.TenantSubscriptions.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId, cancellationToken);

        if (subscription is null || !subscription.IsEnabled(feature))
        {
            var name = mode == PosMode.Retail ? "Point of Sale (Retail)" : "Point of Sale (Restaurant)";
            throw new FeatureNotEnabledException(
                $"This organization does not have the {name} feature enabled, so no location can run a "
                + $"{mode} till. Accounting Features are chosen when the organization is created and "
                + "cannot be changed afterwards.");
        }
    }

    public static async Task<PosTillContext> LoadAsync(
        IAppDbContext db, Guid organizationId, Guid locationId, CancellationToken cancellationToken)
    {
        var location = await db.BillingLocations.SingleOrDefaultAsync(
            x => x.Id == locationId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Billing location not found.");

        if (!location.IsActive)
        {
            throw new ConflictException($"'{location.Name}' is inactive, so its till is closed.");
        }

        if (location.PosMode == PosMode.None)
        {
            throw new ConflictException(
                $"'{location.Name}' does not run a till. Choose a POS mode for it under Configurations > Point of Sale.");
        }

        // A tenant can lose an entitlement after a location was given that mode (phase 41: the plan
        // changes, the mode stays). Switching the till off always works; using it does not.
        await EnsureEntitledAsync(db, organizationId, location.PosMode, cancellationToken);

        var settings = await db.PosLocationSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.BillingLocationId == location.Id, cancellationToken)
            ?? PosLocationSettings.CreateDefault(organizationId, location.Id);

        return new PosTillContext(location, settings);
    }

    /// <summary>
    /// The location and its settings with none of <see cref="LoadAsync"/>'s refusals: what closing a
    /// drawer reads, because a drawer must stay closable after its location stops running a till.
    /// </summary>
    public static async Task<PosTillContext> LoadSettingsAsync(
        IAppDbContext db, Guid organizationId, Guid locationId, CancellationToken cancellationToken)
    {
        var location = await db.BillingLocations.SingleOrDefaultAsync(
            x => x.Id == locationId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Billing location not found.");

        var settings = await db.PosLocationSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.BillingLocationId == location.Id, cancellationToken)
            ?? PosLocationSettings.CreateDefault(organizationId, location.Id);

        return new PosTillContext(location, settings);
    }

    /// <summary>
    /// The drawer's account: the account of the Cash-kind payment mode this till offers (Decision D of
    /// docs/phase-61-status.md). A till with no such mode has no drawer, and one whose cash modes post
    /// to two accounts would have a drawer that is two ledgers at once -- both are refused here, at
    /// open, rather than discovered at close.
    /// </summary>
    public static async Task<Guid> DrawerAccountAsync(
        IAppDbContext db, Guid organizationId, BillingLocation location, CancellationToken cancellationToken)
    {
        var cashAccounts = await (
                from link in db.PosLocationPaymentModes
                join mode in db.PaymentModes on link.PaymentModeId equals mode.Id
                where link.OrganizationId == organizationId && link.BillingLocationId == location.Id
                      && mode.OrganizationId == organizationId
                      && mode.Kind == PaymentModeKind.Cash && mode.IsActive && mode.AccountId != null
                select mode.AccountId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        return cashAccounts.Count switch
        {
            1 => cashAccounts[0],
            0 => throw new ConflictException(
                $"'{location.Name}' offers no Cash payment mode, so its till has no drawer. Link a Cash payment "
                + "mode with a payment account to it under Configurations > Point of Sale."),
            _ => throw new ConflictException(
                $"The Cash payment modes '{location.Name}' offers post to {cashAccounts.Count} different accounts. "
                + "A drawer is one account: point them at the same one, or unlink all but one."),
        };
    }
}
