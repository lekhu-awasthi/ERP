using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.CreateCurrency;

public sealed class CreateCurrencyCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateCurrencyCommand, CreateCurrencyResult>
{
    public async Task<CreateCurrencyResult> Handle(CreateCurrencyCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        var alreadyActivated = await db.Currencies.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Code == code, cancellationToken);

        if (alreadyActivated)
        {
            throw new ConflictException($"{code} is already on this organization's currency list.");
        }

        await EnforceMultiCurrencyEntitlementAsync(request.OrganizationId, cancellationToken);

        var currency = Currency.Create(request.OrganizationId, code, request.Name, request.Symbol);
        db.Currencies.Add(currency);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateCurrencyResult(currency.Id, currency.Code, currency.Name, currency.Symbol);
    }

    /// <summary>
    /// Phase 28, and the second instance of phase-20f Decision #4's shape: <b>the entitlement is a
    /// cap on the currency list, not a block on documents.</b> Every Organization is seeded with the
    /// base currency at creation, so a tenant without MultiCurrency has exactly one and is capped
    /// there; the <i>second</i> currency is what the entitlement buys, precisely as the second
    /// warehouse is.
    ///
    /// <para>This is why no document command in this phase implements <see cref="ErpApp.Application.Common.Security.IRequireFeature"/>.
    /// Confirmed live 2026-09-04: a document's Currency picker is populated from the tenant's own
    /// active currency list, and its Exchange Rate input is disabled and pinned to 1 whenever the
    /// selected currency is the base one. With a one-entry list that surface degenerates to
    /// "NPR, rate 1, read-only" by itself -- FR-2.6's own worked example ("a tenant without
    /// Multi-Currency should not be prompted for exchange rates") is satisfied by the cap, with no
    /// gate on Invoice/Payment/anything. Gating the document commands as well would be a second
    /// enforcement of the same rule, and the one that breaks first when they disagree.</para>
    ///
    /// <para>Conditional, so like the warehouse cap it cannot ride <c>FeatureGateBehavior</c>'s
    /// marker interface and lives here in the handler. Fails closed on a missing subscription row,
    /// same as that behavior.</para>
    /// </summary>
    private async Task EnforceMultiCurrencyEntitlementAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var existingCount = await db.Currencies.CountAsync(x => x.OrganizationId == organizationId, cancellationToken);

        if (existingCount == 0)
        {
            return;
        }

        var enabled = await db.TenantSubscriptions
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => (bool?)x.MultiCurrencyEnabled)
            .SingleOrDefaultAsync(cancellationToken);

        if (enabled != true)
        {
            throw new FeatureNotEnabledException(
                $"This organization does not have the Multi-Currency Support feature enabled, so it is limited to " +
                $"{CurrencyCatalog.BaseCode} only. Accounting Features are chosen when the organization is created " +
                "and cannot be changed afterwards.");
        }
    }
}
