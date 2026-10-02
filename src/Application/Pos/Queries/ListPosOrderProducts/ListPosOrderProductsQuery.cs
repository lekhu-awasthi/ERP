using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.ListPosProducts;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Queries.ListPosOrderProducts;

/// <summary>
/// Phase 64 -- the order screen's product grid: <see cref="ListPosProductsQuery"/>'s grid, read by
/// <see cref="PosSellableProducts"/> exactly as the Retail till reads it, under the waiter's key
/// (<c>Pos.Order.Operate</c>) rather than the cashier's.
/// </summary>
public sealed record ListPosOrderProductsQuery(
    Guid OrganizationId,
    Guid LocationId,
    string? Search = null,
    string? Code = null,
    Guid? CategoryId = null,
    int Page = 1,
    int PageSize = ListPosProductsQuery.DefaultPageSize)
    : IRequest<PagedResult<PosProductDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature, ISearchableQuery
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class ListPosOrderProductsQueryValidator : AbstractValidator<ListPosOrderProductsQuery>
{
    public ListPosOrderProductsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        this.ValidateSearch(x => x.Search);
        RuleFor(x => x.Code).MaximumLength(60);
    }
}

public sealed class ListPosOrderProductsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListPosOrderProductsQuery, PagedResult<PosProductDto>>
{
    public async Task<PagedResult<PosProductDto>> Handle(
        ListPosOrderProductsQuery request, CancellationToken cancellationToken)
    {
        var till = await PosRestaurant.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);
        await PosRestaurant.EnsureMayOrderAtAsync(
            db, request.OrganizationId, currentUser.UserId, till.Location.Id, cancellationToken);

        return await PosSellableProducts.ListAsync(
            db, request.OrganizationId, till.Location.Id, request.Search, request.Code, request.CategoryId,
            request.Page, request.PageSize, cancellationToken);
    }
}
