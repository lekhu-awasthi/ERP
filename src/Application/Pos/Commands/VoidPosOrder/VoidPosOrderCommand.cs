using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Commands.VoidPosOrder;

/// <summary>
/// Phase 64 -- discards a whole order with a reason (the vendor's <c>orders-discard</c>, which takes a
/// <c>void_reason</c>). Whatever is still on it is cancelled to the kitchen, the table is free, and the
/// order stays, so the ERP's POS Orders list shows what was cooked and why it was never billed.
/// Same key and audit as <see cref="VoidPosOrderItems.VoidPosOrderItemsCommand"/>.
/// </summary>
public sealed record VoidPosOrderCommand(Guid OrganizationId, Guid OrderId, string Reason)
    : IRequest<PosOrderDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.PosOrderVoid;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];

    public DocumentType AuditDocumentType => DocumentType.PosOrder;

    public Guid AuditDocumentId => OrderId;
}

public sealed class VoidPosOrderCommandValidator : AbstractValidator<VoidPosOrderCommand>
{
    public VoidPosOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Reason).PosOrderReason();
    }
}

public sealed class VoidPosOrderCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<VoidPosOrderCommand, PosOrderDto>
{
    public async Task<PosOrderDto> Handle(VoidPosOrderCommand request, CancellationToken cancellationToken)
    {
        var (order, _) = await PosRestaurant.LoadOrderForActionAsync(
            db, request.OrganizationId, currentUser.UserId, request.OrderId, cancellationToken);

        var tickets = PosOrderCommands.Run(() => order.Void(request.Reason, currentUser.UserId, DateTimeOffset.UtcNow));

        db.KitchenTickets.AddRange(tickets);
        await db.SaveChangesAsync(cancellationToken);

        return await PosOrderView.ReadAsync(db, order, cancellationToken);
    }
}
