using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Sales.Discounts;
using ErpApp.Application.Sales.DiscountSchemes;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Queries.ListPosTillDiscounts;

/// <summary>
/// Phase 97 -- what the discount schemes give a sale at this till today: each of <paramref name="ProductIds"/>' percent,
/// and the slab ladder for the bill. The Retail till starts a line at its product's percent and the bill at the slab its
/// amount reaches, and the sale's gate keeps exactly these (<see cref="DiscountSchemeRates"/>). Under the cashier's key
/// (<c>Pos.Session.Operate</c>), as the till's other reads are, not <c>Sales.DiscountScheme.View</c>, which a till-only
/// role need not hold. The day is the Nepal day, the sale's own.
/// </summary>
public sealed record ListPosTillDiscountsQuery(Guid OrganizationId, Guid LocationId, IReadOnlyList<Guid> ProductIds)
    : IRequest<ApplicableDiscountsDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSessionOperate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}

public sealed class ListPosTillDiscountsQueryValidator : AbstractValidator<ListPosTillDiscountsQuery>
{
    public ListPosTillDiscountsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.ProductIds).NotNull();
        RuleFor(x => x.ProductIds.Count).LessThanOrEqualTo(DiscountSchemeValidation.MaxLines)
            .When(x => x.ProductIds is not null).OverridePropertyName("ProductIds");
    }
}

public sealed class ListPosTillDiscountsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPosTillDiscountsQuery, ApplicableDiscountsDto>
{
    public async Task<ApplicableDiscountsDto> Handle(ListPosTillDiscountsQuery request, CancellationToken cancellationToken)
    {
        var till = await PosTill.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);
        var rates = await DiscountSchemeRates.LoadAsync(
            db, request.OrganizationId, till.Location.Id, NepalTime.LocalDate(DateTimeOffset.UtcNow), request.ProductIds,
            cancellationToken);

        return new ApplicableDiscountsDto(
            [.. rates.ProductRates.Select(x => new ProductDiscountDto(x.Key, x.Value))], rates.Slabs);
    }
}
