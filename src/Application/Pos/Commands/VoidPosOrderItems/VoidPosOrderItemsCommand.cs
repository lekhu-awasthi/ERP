using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Commands.VoidPosOrderItems;

/// <summary>
/// Phase 64 -- discards quantities the kitchen was already sent, with a reason: the vendor's
/// <i>Discard</i>, whose dialog requires one. The kitchen gets a cancellation ticket per station.
///
/// <para><b>Pos.Order.Void, Admin-only</b> (docs/phase-64-status.md Decision F): it takes cooked food
/// off a bill. Named <c>Void</c> so <c>AuditBehavior</c> writes its row (it audits Create, Update,
/// Approve, Void and Extract verbs only -- phase 61's gotcha); the screen says <i>Discard</i>, as the
/// vendor's does. Nothing unsent is ever on the server, so there is no "discard before sending" here:
/// that is the browser dropping a line it has not sent.</para>
/// </summary>
public sealed record VoidPosOrderItemsCommand(
    Guid OrganizationId,
    Guid OrderId,
    IReadOnlyList<PosOrderLineQuantityInput> Items,
    string Reason)
    : IRequest<PosOrderDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.PosOrderVoid;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];

    public DocumentType AuditDocumentType => DocumentType.PosOrder;

    public Guid AuditDocumentId => OrderId;
}

public sealed class VoidPosOrderItemsCommandValidator : AbstractValidator<VoidPosOrderItemsCommand>
{
    public VoidPosOrderItemsCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        this.ValidateLineQuantities(x => x.Items, "Choose what to discard.");
        RuleFor(x => x.Reason).PosOrderReason();
    }
}

public sealed class VoidPosOrderItemsCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<VoidPosOrderItemsCommand, PosOrderDto>
{
    public async Task<PosOrderDto> Handle(VoidPosOrderItemsCommand request, CancellationToken cancellationToken)
    {
        var (order, _) = await PosRestaurant.LoadOrderForActionAsync(
            db, request.OrganizationId, currentUser.UserId, request.OrderId, cancellationToken);

        // Phase 65 -- never what is billed; and discarding the unbilled rest of a part-billed order settles it.
        var invoiced = (await PosOrderBilling.LoadAsync(db, request.OrganizationId, order.Id, cancellationToken)).Invoiced;

        var tickets = PosOrderCommands.Run(() => order.Discard(
            [.. request.Items.Select(x => new PosOrderLineQuantity(x.LineId, x.Quantity))],
            request.Reason,
            currentUser.UserId,
            invoiced));

        order.SettleIfFullyBilled(invoiced, DateTimeOffset.UtcNow);

        db.KitchenTickets.AddRange(tickets);
        await db.SaveChangesAsync(cancellationToken);

        return await PosOrderView.ReadAsync(db, order, cancellationToken);
    }
}
