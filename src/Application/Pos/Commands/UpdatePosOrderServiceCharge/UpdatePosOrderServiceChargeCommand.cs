using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Commands.UpdatePosOrderServiceCharge;

/// <summary>
/// Phase 72 -- the guest declined the service charge (<see cref="Waived"/>), or changed their mind. A mandatory
/// service charge has been void since the Supreme Court's ruling of 2023-01-25 (phase-61-status.md Decision A), so
/// a guest may refuse it; the vendor offers this as <i>Disable Service Charge</i>. Every line is then billed
/// without it while keeping its own frozen rate. Fixed once any of the order is billed (a 409).
///
/// <para>Permissions as <see cref="UpdatePosOrderDiscount.UpdatePosOrderDiscountCommand"/>'s, with its own key since
/// phase 76: <c>Sales.Invoice.TillRemoveServiceCharge</c> at the order's location, the vendor's <i>Can Remove Service
/// Charge</i> beside <i>Can Give Discount</i>. Restoring the charge needs it too: a gate on a bill's terms is about
/// the terms, not the direction (phase 74).</para>
/// </summary>
public sealed record UpdatePosOrderServiceChargeCommand(Guid OrganizationId, Guid OrderId, bool? Waived)
    : IRequest<PosOrderDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];

    public DocumentType AuditDocumentType => DocumentType.PosOrder;

    public Guid AuditDocumentId => OrderId;
}

public sealed class UpdatePosOrderServiceChargeCommandValidator : AbstractValidator<UpdatePosOrderServiceChargeCommand>
{
    public UpdatePosOrderServiceChargeCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();

        // Required: an absent or misspelt field must not read as "charge it".
        RuleFor(x => x.Waived).NotNull().WithMessage("Say whether the guest declined the service charge.");
    }
}

public sealed class UpdatePosOrderServiceChargeCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UpdatePosOrderServiceChargeCommand, PosOrderDto>
{
    public async Task<PosOrderDto> Handle(UpdatePosOrderServiceChargeCommand request, CancellationToken cancellationToken)
    {
        var (order, invoiced) = await PosOrderTerms.LoadForChangeAsync(
            db, request.OrganizationId, currentUser.UserId, request.OrderId, PermissionKeys.InvoiceTillRemoveServiceCharge,
            cancellationToken);

        PosOrderCommands.Run(() => order.WaiveServiceCharge(request.Waived!.Value, invoiced));
        await db.SaveChangesAsync(cancellationToken);

        return await PosOrderView.ReadAsync(db, order, cancellationToken);
    }
}
