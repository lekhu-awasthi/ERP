using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Queries.GetPosOrder;

/// <summary>
/// Phase 64 -- one order as the till's order screen shows it: its lines with every quantity, and its
/// kitchen tickets. Read whatever its state, so an order voided at one till is seen as voided at another.
/// </summary>
public sealed record GetPosOrderQuery(Guid OrganizationId, Guid OrderId)
    : IRequest<PosOrderDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class GetPosOrderQueryValidator : AbstractValidator<GetPosOrderQuery>
{
    public GetPosOrderQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
    }
}

public sealed class GetPosOrderQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetPosOrderQuery, PosOrderDto>
{
    public async Task<PosOrderDto> Handle(GetPosOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await PosRestaurant.LoadOrderAsync(db, request.OrganizationId, request.OrderId, cancellationToken);
        await PosRestaurant.EnsureMayOrderAtAsync(
            db, request.OrganizationId, currentUser.UserId, order.BillingLocationId, cancellationToken);

        return await PosOrderView.ReadAsync(db, order, cancellationToken);
    }
}
