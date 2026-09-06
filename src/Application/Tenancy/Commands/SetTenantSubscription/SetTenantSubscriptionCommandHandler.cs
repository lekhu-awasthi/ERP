using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Tenancy.Queries.GetTenantSubscription;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.SetTenantSubscription;

public sealed class SetTenantSubscriptionCommandHandler(IAppDbContext db)
    : IRequestHandler<SetTenantSubscriptionCommand, TenantSubscriptionDto>
{
    public async Task<TenantSubscriptionDto> Handle(
        SetTenantSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await db.TenantSubscriptions.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("This organization has no subscription record.");

        subscription.Renew(request.PlanName, request.EndsAt);
        await db.SaveChangesAsync(cancellationToken);

        return GetTenantSubscriptionQueryHandler.ToDto(subscription);
    }
}
