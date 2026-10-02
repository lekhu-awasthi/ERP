using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Queries.PreviewPosOrderBill;

/// <summary>
/// Phase 65 -- what a part of an order would come to, priced by the same planner the bill posts through
/// (phase-65-status.md Decision E). The till's split screen shows it before money changes hands, and the
/// order screen prints it as the <b>estimate bill</b> (the whole remainder; not a tax invoice, and not a
/// print of one).
///
/// <para>Under <c>Pos.Order.Operate</c> at the order's location, like every order read: a waiter who
/// holds no drawer prints the estimate the guests ask for, and only the cashier's bill needs a session.
/// A POST because the part is a body (items with quantities), and nothing is written.</para>
/// </summary>
public sealed record PreviewPosOrderBillQuery(
    Guid OrganizationId,
    Guid OrderId,
    PosOrderSplit Split,
    IReadOnlyList<PosOrderLineQuantityInput> Items,
    int? Parts)
    : IRequest<PosOrderBillPreviewDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class PreviewPosOrderBillQueryValidator : AbstractValidator<PreviewPosOrderBillQuery>
{
    public PreviewPosOrderBillQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Split).IsInEnum();
        this.ValidateLineQuantities(x => x.Items);
        RuleFor(x => x.Items.Count)
            .GreaterThan(0)
            .When(x => x.Split == PosOrderSplit.Items && x.Items != null)
            .OverridePropertyName(nameof(PreviewPosOrderBillQuery.Items))
            .WithMessage("Choose what goes on this bill.");
        RuleFor(x => x.Parts)
            .NotNull()
            .InclusiveBetween(1, PosOrder.MaxCovers)
            .When(x => x.Split == PosOrderSplit.Equal)
            .WithMessage($"An equal split is into 1 to {PosOrder.MaxCovers} parts.");
    }
}

public sealed class PreviewPosOrderBillQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<PreviewPosOrderBillQuery, PosOrderBillPreviewDto>
{
    public async Task<PosOrderBillPreviewDto> Handle(PreviewPosOrderBillQuery request, CancellationToken cancellationToken)
    {
        var (order, till) = await PosRestaurant.LoadOrderForActionAsync(
            db, request.OrganizationId, currentUser.UserId, request.OrderId, cancellationToken);

        var (plan, _) = await PosOrderBillPlanner.PlanAsync(
            db, request.OrganizationId, order, till, new PosOrderBillPart(request.Split, request.Items, request.Parts),
            cancellationToken);

        return await PosOrderBillPlanner.ToPreviewAsync(db, order, plan, cancellationToken);
    }
}
