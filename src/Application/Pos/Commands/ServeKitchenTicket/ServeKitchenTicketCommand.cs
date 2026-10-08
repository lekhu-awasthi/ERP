using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Commands.ServeKitchenTicket;

/// <summary>What the order's tickets stand at after the serve.</summary>
public sealed record ServeKitchenTicketResult(Guid OrderId, Guid TicketId, KitchenTicketState State, decimal Pending);

/// <summary>
/// Phase 65 -- the kitchen board's <i>Served</i>: marks a ticket's items as having left the pass, all of
/// what it still has to cook when <see cref="Items"/> is empty, or the quantities named (the vendor's
/// card-level serve dialog and its per-item <i>Mark as Served</i>). It writes the same served counter a
/// waiter's Serve does (phase 64 Decision B), so the board and the order screen cannot disagree.
///
/// <para>Under <c>Pos.Kitchen.Operate</c>: a cook marks food served without holding a waiter's key, or any
/// <c>Sales.Invoice</c> key (Decision G). A quantity above what the ticket still has to cook is a 409, and
/// so is serving a cancellation.</para>
/// </summary>
public sealed record ServeKitchenTicketCommand(
    Guid OrganizationId,
    Guid OrderId,
    Guid TicketId,
    IReadOnlyList<PosOrderLineQuantityInput> Items)
    : IRequest<ServeKitchenTicketResult>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosKitchenOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class ServeKitchenTicketCommandValidator : AbstractValidator<ServeKitchenTicketCommand>
{
    public ServeKitchenTicketCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.TicketId).NotEmpty();
        this.ValidateLineQuantities(x => x.Items);
    }
}

public sealed class ServeKitchenTicketCommandHandler(IAppDbContext db)
    : IRequestHandler<ServeKitchenTicketCommand, ServeKitchenTicketResult>
{
    public async Task<ServeKitchenTicketResult> Handle(ServeKitchenTicketCommand request, CancellationToken cancellationToken)
    {
        var order = await PosRestaurant.LoadOrderAsync(db, request.OrganizationId, request.OrderId, cancellationToken);
        await PosRestaurant.LoadAsync(db, request.OrganizationId, order.BillingLocationId, cancellationToken);

        var progress = order.TicketProgress().SingleOrDefault(x => x.TicketId == request.TicketId)
            ?? throw new NotFoundException("Kitchen ticket not found.");

        if (progress.State is KitchenTicketState.Cancellation or KitchenTicketState.Moved)
        {
            throw new ConflictException(progress.State == KitchenTicketState.Moved
                ? "That food was parcelled or moved to another table; serve it from the ticket that moved it."
                : "A cancellation ticket has nothing to serve.");
        }

        var pending = progress.Lines.ToDictionary(x => x.OrderLineId, x => x.Pending);

        IReadOnlyList<PosOrderLineQuantity> items = request.Items.Count == 0
            ? [.. pending.Where(x => x.Value > 0m).Select(x => new PosOrderLineQuantity(x.Key, x.Value))]
            : [.. request.Items.Select(x => new PosOrderLineQuantity(x.LineId, x.Quantity))];

        if (items.Count == 0)
        {
            throw new ConflictException("Everything on this ticket has been served.");
        }

        foreach (var item in items)
        {
            if (!pending.TryGetValue(item.LineId, out var left))
            {
                throw new ConflictException("That item is not on this ticket.");
            }

            if (item.Quantity > left)
            {
                throw new ConflictException($"This ticket has {left:0.####} of that item still to serve, not {item.Quantity:0.####}.");
            }
        }

        PosOrderCommands.Run(() => order.Serve(items));

        await db.SaveChangesAsync(cancellationToken);

        var after = order.TicketProgress().Single(x => x.TicketId == request.TicketId);
        return new ServeKitchenTicketResult(order.Id, after.TicketId, after.State, after.Lines.Sum(x => x.Pending));
    }
}
