using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Tenancy.Queries.GetTenantSubscription;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.SetTenantSubscription;

public sealed class SetTenantSubscriptionCommandHandler(IAppDbContext db)
    : IRequestHandler<SetTenantSubscriptionCommand, TenantSubscriptionDto>
{
    /// <summary>The plan name recorded for a term with no catalogue plan behind it -- a trial, or an
    /// extension of one. The same literal <c>CreateTrial</c> seeds, so the two paths cannot drift into
    /// two spellings of the same state.</summary>
    private const string NoPlanName = "Trial";

    public async Task<TenantSubscriptionDto> Handle(
        SetTenantSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await db.TenantSubscriptions.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("This organization has no subscription record.");

        // A plan id that names no catalogue row is a 404 rather than a silent fall-through to the
        // trial defaults: the caller asked for a specific plan and would otherwise be told the
        // subscription was set while getting something else entirely.
        var plan = request.PlanId is { } planId
            ? await db.SubscriptionPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId, cancellationToken)
                ?? throw new NotFoundException("That subscription plan does not exist.")
            : null;

        // Each commercial term defaults to the plan's published value and is overridable, because
        // what a tenant was actually sold is routinely not the list price: add-ons raise the two
        // ceilings, and a negotiated or multi-year rate moves the amount. With no plan there is
        // nothing to default from, and the defaults are the unmetered trial's.
        var amount = request.SubscriptionAmount ?? plan?.AnnualAmount ?? 0m;
        var productQuota = request.ProductQuota ?? plan?.ProductQuota ?? 0;
        var transactionQuota = request.TransactionQuota ?? plan?.TransactionQuota ?? 0;

        // The term starts now. It is a Domain parameter rather than a Domain UtcNow so that
        // back-dating stays expressible, but nothing in this product back-dates one yet and letting
        // a caller choose would let a tenant re-open a spent transaction allowance at will.
        subscription.SetPlan(
            plan?.Id,
            plan?.Name ?? NoPlanName,
            DateTimeOffset.UtcNow,
            request.EndsAt,
            amount,
            productQuota,
            transactionQuota,
            request.IrdVerified);

        await db.SaveChangesAsync(cancellationToken);

        var usage = await SubscriptionUsageReader.ReadAsync(db, subscription, cancellationToken);

        return GetTenantSubscriptionQueryHandler.ToDto(subscription, usage);
    }
}
