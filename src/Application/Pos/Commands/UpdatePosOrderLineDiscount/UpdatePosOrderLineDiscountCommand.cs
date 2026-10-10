using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Commands.UpdatePosOrderLineDiscount;

/// <summary>
/// Phase 72 -- one dish's discount: a percentage off that line before the order's (the vendor's <i>Edit Item &gt;
/// Discount (Item Line)</i>). Fixed once any of that line is billed (a 409); the rest of the order may already be.
/// Phase 74 -- or <paramref name="DiscountAmount"/> rupees off each unit, as the vendor's <i>Amount</i> takes it.
///
/// <para>Permissions as <see cref="UpdatePosOrderDiscount.UpdatePosOrderDiscountCommand"/>'s: Pos.Order.Operate in
/// the pipeline, Sales.Invoice.TillDiscount at the order's location in the handler (phase 76).</para>
/// </summary>
public sealed record UpdatePosOrderLineDiscountCommand(
    Guid OrganizationId, Guid OrderId, Guid LineId, decimal? DiscountPct, decimal? DiscountAmount = null)
    : IRequest<PosOrderDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];

    public DocumentType AuditDocumentType => DocumentType.PosOrder;

    public Guid AuditDocumentId => OrderId;
}

public sealed class UpdatePosOrderLineDiscountCommandValidator : AbstractValidator<UpdatePosOrderLineDiscountCommand>
{
    public UpdatePosOrderLineDiscountCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.LineId).NotEmpty();
        RuleFor(x => x.DiscountPct).NotNull().When(x => x.DiscountAmount is null)
            .WithMessage(PosOrderValidationRules.OneKindOfDiscount);
        RuleFor(x => x.DiscountAmount).Null().When(x => x.DiscountPct is not null)
            .WithMessage(PosOrderValidationRules.OneKindOfDiscount);
        RuleFor(x => x.DiscountPct).PosOrderDiscountPct();
        RuleFor(x => x.DiscountAmount).PosCashAmount();
    }
}

public sealed class UpdatePosOrderLineDiscountCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UpdatePosOrderLineDiscountCommand, PosOrderDto>
{
    public async Task<PosOrderDto> Handle(UpdatePosOrderLineDiscountCommand request, CancellationToken cancellationToken)
    {
        var (order, invoiced) = await PosOrderTerms.LoadForChangeAsync(
            db, request.OrganizationId, currentUser.UserId, request.OrderId, PermissionKeys.InvoiceTillDiscount,
            cancellationToken);

        var (kind, value) = request.DiscountAmount is { } amount
            ? (PosDiscountKind.Amount, amount)
            : (PosDiscountKind.Percent, request.DiscountPct!.Value);

        PosOrderCommands.Run(() => order.SetLineDiscount(request.LineId, kind, value, invoiced));
        await db.SaveChangesAsync(cancellationToken);

        return await PosOrderView.ReadAsync(db, order, cancellationToken);
    }
}
