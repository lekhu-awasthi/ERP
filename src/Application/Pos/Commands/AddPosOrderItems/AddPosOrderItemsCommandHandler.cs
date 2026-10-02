using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using MediatR;

namespace ErpApp.Application.Pos.Commands.AddPosOrderItems;

public sealed class AddPosOrderItemsCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<AddPosOrderItemsCommand, PosOrderDto>
{
    public async Task<PosOrderDto> Handle(AddPosOrderItemsCommand request, CancellationToken cancellationToken)
    {
        var (order, till) = await PosRestaurant.LoadOrderForActionAsync(
            db, request.OrganizationId, currentUser.UserId, request.OrderId, cancellationToken);

        var newLines = await PosOrderPricing.PriceAsync(
            db, till, order.OrderType, request.NewItems, nameof(request.NewItems), cancellationToken);

        var result = PosOrderCommands.Run(() => order.Send(
            newLines,
            [.. request.MoreOf.Select(x => new PosOrderLineQuantity(x.LineId, x.Quantity))],
            currentUser.UserId));

        // Children appended to a tracked aggregate are added through their own sets (phase 24).
        db.PosOrderLines.AddRange(result.NewLines);
        db.KitchenTickets.AddRange(result.NewTickets);

        await db.SaveChangesAsync(cancellationToken);

        return await PosOrderView.ReadAsync(db, order, cancellationToken);
    }
}
