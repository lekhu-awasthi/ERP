using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Commands.UpdatePosOrderDiscount;

/// <summary>
/// Phase 72 -- the order's discount: a percentage off every line after the line's own (the vendor's
/// <i>Options &gt; Discount</i>, read live on 2026-10-09). Fixed once any of the order is billed (a 409), so every
/// part is priced at the same terms and the bills still add up to the order.
/// Phase 74 -- or <paramref name="DiscountAmount"/> rupees off the whole tab, spread over its dishes and frozen at
/// its first bill (<see cref="Domain.Pos.PosOrder.DiscountAmount"/>).
///
/// <para><b>Pos.Order.Operate</b> in the pipeline, as every order action, and <b>Sales.Invoice.TillDiscount</b> at
/// the order's location in the handler (<see cref="PosOrderTerms"/>; phase 72 used Approve, phase 76 split it out as
/// the vendor's <i>Can Give Discount</i>).
/// Named <c>Update</c> so <c>AuditBehavior</c> writes its row.</para>
/// </summary>
public sealed record UpdatePosOrderDiscountCommand(
    Guid OrganizationId, Guid OrderId, decimal? DiscountPct, decimal? DiscountAmount = null)
    : IRequest<PosOrderDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];

    public DocumentType AuditDocumentType => DocumentType.PosOrder;

    public Guid AuditDocumentId => OrderId;
}

public sealed class UpdatePosOrderDiscountCommandValidator : AbstractValidator<UpdatePosOrderDiscountCommand>
{
    public UpdatePosOrderDiscountCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.DiscountPct).NotNull().When(x => x.DiscountAmount is null)
            .WithMessage(PosOrderValidationRules.OneKindOfDiscount);
        RuleFor(x => x.DiscountAmount).Null().When(x => x.DiscountPct is not null)
            .WithMessage(PosOrderValidationRules.OneKindOfDiscount);
        RuleFor(x => x.DiscountPct).PosOrderDiscountPct();
        RuleFor(x => x.DiscountAmount).PosCashAmount();
    }
}

public sealed class UpdatePosOrderDiscountCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UpdatePosOrderDiscountCommand, PosOrderDto>
{
    public async Task<PosOrderDto> Handle(UpdatePosOrderDiscountCommand request, CancellationToken cancellationToken)
    {
        var (order, invoiced) = await PosOrderTerms.LoadForChangeAsync(
            db, request.OrganizationId, currentUser.UserId, request.OrderId, PermissionKeys.InvoiceTillDiscount,
            async loaded => request.DiscountAmount is null && request.DiscountPct > 0m
                && request.DiscountPct == (await PosOrderSchemes.LoadAsync(db, loaded, cancellationToken))
                    .ForBillAmount(loaded.AmountBeforeOrderDiscount),
            cancellationToken);

        var (kind, value) = request.DiscountAmount is { } amount
            ? (PosDiscountKind.Amount, amount)
            : (PosDiscountKind.Percent, request.DiscountPct!.Value);

        PosOrderCommands.Run(() => order.SetDiscount(kind, value, invoiced));
        await db.SaveChangesAsync(cancellationToken);

        return await PosOrderView.ReadAsync(db, order, cancellationToken);
    }
}
