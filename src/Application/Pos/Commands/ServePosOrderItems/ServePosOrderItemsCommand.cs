using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Commands.ServePosOrderItems;

/// <summary>
/// Phase 64 -- marks quantities as served, partially if need be: the vendor's <i>Mark as Served</i>,
/// which takes a quantity per item. Only what is outstanding can be served (a 409 otherwise).
/// </summary>
public sealed record ServePosOrderItemsCommand(
    Guid OrganizationId,
    Guid OrderId,
    IReadOnlyList<PosOrderLineQuantityInput> Items)
    : IRequest<PosOrderDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class ServePosOrderItemsCommandValidator : AbstractValidator<ServePosOrderItemsCommand>
{
    public ServePosOrderItemsCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        this.ValidateLineQuantities(x => x.Items, "Choose what was served.");
    }
}

public sealed class ServePosOrderItemsCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ServePosOrderItemsCommand, PosOrderDto>
{
    public async Task<PosOrderDto> Handle(ServePosOrderItemsCommand request, CancellationToken cancellationToken)
    {
        var (order, _) = await PosRestaurant.LoadOrderForActionAsync(
            db, request.OrganizationId, currentUser.UserId, request.OrderId, cancellationToken);

        PosOrderCommands.Run(() => order.Serve(
            [.. request.Items.Select(x => new PosOrderLineQuantity(x.LineId, x.Quantity))]));

        await db.SaveChangesAsync(cancellationToken);

        return await PosOrderView.ReadAsync(db, order, cancellationToken);
    }
}
