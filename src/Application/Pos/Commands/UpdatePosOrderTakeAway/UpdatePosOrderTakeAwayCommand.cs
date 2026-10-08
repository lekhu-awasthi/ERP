using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Commands.UpdatePosOrderTakeAway;

/// <summary>
/// Phase 68 -- parcels part of a dine-in line: the vendor's <i>Mark as Take Away</i>. The quantity moves to
/// the line's take-away sibling, whose service charge is decided now from the location's <i>Service charge
/// on take-away</i> and frozen (docs/phase-68-status.md Decision B). The kitchen gets a take-away ticket.
///
/// <para><b>Pos.Order.Operate</b> at the order's location, as a send is: it is routine table work, and the
/// service charge it may take off is the location's setting, not the waiter's choice. Named <c>Update</c> so
/// <c>AuditBehavior</c> writes its row, because it can change what the table pays.</para>
/// </summary>
public sealed record UpdatePosOrderTakeAwayCommand(
    Guid OrganizationId,
    Guid OrderId,
    Guid LineId,
    decimal Quantity)
    : IRequest<PosOrderDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];

    public DocumentType AuditDocumentType => DocumentType.PosOrder;

    public Guid AuditDocumentId => OrderId;
}

public sealed class UpdatePosOrderTakeAwayCommandValidator : AbstractValidator<UpdatePosOrderTakeAwayCommand>
{
    public UpdatePosOrderTakeAwayCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.LineId).NotEmpty();
        RuleFor(x => x.Quantity).PosOrderQuantity();
    }
}

public sealed class UpdatePosOrderTakeAwayCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UpdatePosOrderTakeAwayCommand, PosOrderDto>
{
    public async Task<PosOrderDto> Handle(UpdatePosOrderTakeAwayCommand request, CancellationToken cancellationToken)
    {
        var (order, till) = await PosRestaurant.LoadOrderForActionAsync(
            db, request.OrganizationId, currentUser.UserId, request.OrderId, cancellationToken);

        // Billed food is on a tax invoice at its rate, so it cannot be parcelled into another one.
        var invoiced = (await PosOrderBilling.LoadAsync(db, request.OrganizationId, order.Id, cancellationToken)).Invoiced;

        var result = PosOrderCommands.Run(() => order.MarkTakeAway(
            request.LineId, request.Quantity, till.Settings.ServiceChargeOnTakeAway, currentUser.UserId, invoiced));

        // Added through the sets: a child appended to a tracked parent reads as Modified (phase 24).
        db.PosOrderLines.AddRange(result.NewLines);
        db.KitchenTickets.AddRange(result.NewTickets);
        await db.SaveChangesAsync(cancellationToken);

        return await PosOrderView.ReadAsync(db, order, cancellationToken);
    }
}
