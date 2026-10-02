using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.CreatePosOrder;

public sealed class CreatePosOrderCommandHandler(
    IAppDbContext db, IDocumentNumberGenerator numberGenerator, ICurrentUserService currentUser)
    : IRequestHandler<CreatePosOrderCommand, PosOrderDto>
{
    public async Task<PosOrderDto> Handle(CreatePosOrderCommand request, CancellationToken cancellationToken)
    {
        var till = await PosRestaurant.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);
        await PosRestaurant.EnsureMayOrderAtAsync(
            db, request.OrganizationId, currentUser.UserId, till.Location.Id, cancellationToken);

        if (!PosTabs.For(till.Location.PosMode).Contains(request.OrderType))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.OrderType), $"'{till.Location.Name}' does not take {PosOrder.Describe(request.OrderType)} orders.")]);
        }

        if (request.TableId is { } tableId)
        {
            var table = await PosRestaurant.LoadSeatableTableAsync(
                db, request.OrganizationId, till.Location.Id, tableId, nameof(request.TableId), cancellationToken);
            await PosRestaurant.EnsureTableFreeAsync(db, request.OrganizationId, table, cancellationToken);
        }

        await EnsureContactAsync(request, cancellationToken);

        var lines = await PosOrderPricing.PriceAsync(
            db, till, request.OrderType, request.Items, nameof(request.Items), cancellationToken);

        var number = await numberGenerator.GetNextNumberAsync(
            request.OrganizationId, DocumentType.PosOrder, cancellationToken);

        var order = PosOrder.Open(
            request.OrganizationId, till.Location.Id, PosOrder.CodePrefix + number, request.OrderType,
            request.TableId, request.Covers, request.ContactId, currentUser.UserId, DateTimeOffset.UtcNow);

        // A new order's lines and tickets ride in with it as one Added graph.
        order.Send(lines, [], currentUser.UserId);
        db.PosOrders.Add(order);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (request.TableId is { } seated)
        {
            // The filtered unique index caught a second waiter seating the same table at the same moment;
            // the read above cannot, because both requests passed it before either saved.
            var code = await PosRestaurant.OpenOrderCodeAtAsync(db, request.OrganizationId, seated, cancellationToken);
            if (code is null)
            {
                throw;
            }

            throw new ConflictException($"That table already has order {code} open. Open it, or seat the guests elsewhere.");
        }

        return await PosOrderView.ReadAsync(db, order, cancellationToken);
    }

    /// <summary>A named customer must exist and be active; a Delivery's may not be the walk-in, because
    /// the address it goes to is the customer's, and the walk-in has none.</summary>
    private async Task EnsureContactAsync(CreatePosOrderCommand request, CancellationToken cancellationToken)
    {
        if (request.ContactId is not { } contactId)
        {
            return;
        }

        var contact = await db.Contacts
            .Where(x => x.Id == contactId && x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.IsActive, x.IsWalkInCustomer, x.Name })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException([new ValidationFailure(nameof(request.ContactId), "That customer does not exist.")]);

        if (!contact.IsActive)
        {
            throw new ConflictException($"'{contact.Name}' is inactive, so no order can be opened for them.");
        }

        if (request.OrderType == PosTab.Delivery && contact.IsWalkInCustomer)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.ContactId), "A Delivery order names a customer with an address, not the walk-in.")]);
        }
    }
}
