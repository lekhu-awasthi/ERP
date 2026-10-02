using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.UpdatePosOrder;

/// <summary>
/// Phase 64 -- an open order's header: its table (the vendor's table transfer), its guests and its
/// customer. The whole header, every time, so nothing binds to a default in silence (phase 27b).
/// Audited (Update): moving a tab between tables is exactly the change an audit trail is for.
/// </summary>
public sealed record UpdatePosOrderCommand(
    Guid OrganizationId,
    Guid OrderId,
    Guid? TableId,
    int Covers,
    Guid? ContactId)
    : IRequest<PosOrderDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];

    public DocumentType AuditDocumentType => DocumentType.PosOrder;

    public Guid AuditDocumentId => OrderId;
}

public sealed class UpdatePosOrderCommandValidator : AbstractValidator<UpdatePosOrderCommand>
{
    public UpdatePosOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Covers).InclusiveBetween(0, PosOrder.MaxCovers);
    }
}

public sealed class UpdatePosOrderCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UpdatePosOrderCommand, PosOrderDto>
{
    public async Task<PosOrderDto> Handle(UpdatePosOrderCommand request, CancellationToken cancellationToken)
    {
        var (order, till) = await PosRestaurant.LoadOrderForActionAsync(
            db, request.OrganizationId, currentUser.UserId, request.OrderId, cancellationToken);

        if (order.OrderType == PosTab.DineIn && request.TableId is null)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.TableId), "A Dine In order is seated at a table.")]);
        }

        if (order.OrderType != PosTab.DineIn && request.TableId is not null)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.TableId), $"A {PosOrder.Describe(order.OrderType)} order has no table.")]);
        }

        var moving = request.TableId is { } tableId && tableId != order.PosTableId;
        if (moving)
        {
            var table = await PosRestaurant.LoadSeatableTableAsync(
                db, request.OrganizationId, till.Location.Id, request.TableId!.Value, nameof(request.TableId),
                cancellationToken);
            await PosRestaurant.EnsureTableFreeAsync(db, request.OrganizationId, table, cancellationToken);
        }

        if (request.ContactId is { } contactId && contactId != order.ContactId)
        {
            var contact = await db.Contacts
                .Where(x => x.Id == contactId && x.OrganizationId == request.OrganizationId)
                .Select(x => new { x.IsActive, x.IsWalkInCustomer, x.Name })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new ValidationException([new ValidationFailure(nameof(request.ContactId), "That customer does not exist.")]);

            if (!contact.IsActive)
            {
                throw new ConflictException($"'{contact.Name}' is inactive.");
            }

            if (order.OrderType == PosTab.Delivery && contact.IsWalkInCustomer)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.ContactId), "A Delivery order names a customer with an address, not the walk-in.")]);
            }
        }

        PosOrderCommands.Run(() =>
        {
            if (moving)
            {
                order.MoveToTable(request.TableId!.Value);
            }

            order.UpdateDetails(request.Covers, request.ContactId);
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (moving)
        {
            var code = await PosRestaurant.OpenOrderCodeAtAsync(
                db, request.OrganizationId, request.TableId!.Value, cancellationToken);
            if (code is null)
            {
                throw;
            }

            throw new ConflictException($"That table already has order {code} open.");
        }

        return await PosOrderView.ReadAsync(db, order, cancellationToken);
    }
}
