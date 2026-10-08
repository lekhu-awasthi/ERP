using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
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

namespace ErpApp.Application.Pos.Commands.CreatePosOrderTransfer;

/// <summary>
/// Phase 68 -- moves items from one Dine In order to another table: the vendor's <i>Transfer Items</i>
/// (<c>POST /pos/orders/items-transfer {order_id, table_id, area_id, items}</c>). The target is the open
/// order at that table, or a new Dine In order opened there for the walk-in, one guest
/// (docs/phase-68-status.md Decision D).
///
/// <para><b>Pos.Order.Operate</b> at the order's location (the table is on the same floor, so the same
/// branch boundary covers both orders). Named <c>Create</c> so <c>AuditBehavior</c> writes its row: a
/// transfer moves a charge from one guest's tab to another's.</para>
/// </summary>
public sealed record CreatePosOrderTransferCommand(
    Guid OrganizationId,
    Guid OrderId,
    Guid TableId,
    IReadOnlyList<PosOrderLineQuantityInput> Items)
    : IRequest<PosOrderTransferDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];

    public DocumentType AuditDocumentType => DocumentType.PosOrder;

    public Guid AuditDocumentId => OrderId;
}

/// <summary>Both orders after the transfer. <paramref name="TargetCreated"/> says the table had no open
/// order and one was opened for it; the source may come back voided, if the transfer emptied it.</summary>
public sealed record PosOrderTransferDto(PosOrderDto Source, PosOrderDto Target, bool TargetCreated);

public sealed class CreatePosOrderTransferCommandValidator : AbstractValidator<CreatePosOrderTransferCommand>
{
    public CreatePosOrderTransferCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.TableId).NotEmpty();
        this.ValidateLineQuantities(x => x.Items, "Choose what to transfer.");
    }
}

public sealed class CreatePosOrderTransferCommandHandler(
    IAppDbContext db, IDocumentNumberGenerator numberGenerator, ICurrentUserService currentUser)
    : IRequestHandler<CreatePosOrderTransferCommand, PosOrderTransferDto>
{
    public async Task<PosOrderTransferDto> Handle(CreatePosOrderTransferCommand request, CancellationToken cancellationToken)
    {
        var (source, till) = await PosRestaurant.LoadOrderForActionAsync(
            db, request.OrganizationId, currentUser.UserId, request.OrderId, cancellationToken);

        if (source.OrderType != PosTab.DineIn)
        {
            throw new ConflictException($"A {PosOrder.Describe(source.OrderType)} order has no table to transfer from.");
        }

        if (request.TableId == source.PosTableId)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.TableId), "Choose another table: the items are already at this one.")]);
        }

        var table = await PosRestaurant.LoadSeatableTableAsync(
            db, request.OrganizationId, till.Location.Id, request.TableId, nameof(request.TableId), cancellationToken);

        var targetId = await db.PosOrders
            .Where(x => x.OrganizationId == request.OrganizationId && x.PosTableId == table.Id && x.Status == PosOrderStatus.Open)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        PosOrder target;

        if (targetId is { } id)
        {
            target = await PosRestaurant.LoadOrderAsync(db, request.OrganizationId, id, cancellationToken);
        }
        else
        {
            var number = await numberGenerator.GetNextNumberAsync(
                request.OrganizationId, DocumentType.PosOrder, cancellationToken);
            target = PosOrder.Open(
                request.OrganizationId, till.Location.Id, PosOrder.CodePrefix + number, PosTab.DineIn,
                table.Id, covers: 1, contactId: null, currentUser.UserId, now);
        }

        var invoiced = (await PosOrderBilling.LoadAsync(db, request.OrganizationId, source.Id, cancellationToken)).Invoiced;

        var result = PosOrderCommands.Run(() => PosOrder.Transfer(
            source, target,
            [.. request.Items.Select(x => new PosOrderLineQuantity(x.LineId, x.Quantity))],
            currentUser.UserId, invoiced, now));

        // A transfer that leaves the source fully billed settles it, as a discard of the rest does.
        if (!result.SourceEmptied)
        {
            source.SettleIfFullyBilled(invoiced, now);
        }

        // A new target joins the context only once the transfer succeeded, so a refusal leaves nothing behind.
        if (targetId is null)
        {
            db.PosOrders.Add(target);
        }

        db.PosOrderLines.AddRange(result.Target.NewLines);
        db.KitchenTickets.AddRange(result.Source.NewTickets);
        db.KitchenTickets.AddRange(result.Target.NewTickets);
        target.Touch();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (targetId is null)
        {
            // Another waiter seated the table between the read and the save; the filtered unique index won.
            var code = await PosRestaurant.OpenOrderCodeAtAsync(db, request.OrganizationId, table.Id, cancellationToken);
            if (code is null)
            {
                throw;
            }

            throw new ConflictException($"Table {table.Name} has just had order {code} opened. Try the transfer again.");
        }

        var views = await PosOrderView.ReadManyAsync(db, request.OrganizationId, [source, target], cancellationToken);
        return new PosOrderTransferDto(views[0], views[1], targetId is null);
    }
}
