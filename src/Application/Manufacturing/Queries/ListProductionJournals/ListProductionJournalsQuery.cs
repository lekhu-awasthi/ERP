using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.ListProductionJournals;

public sealed record ListProductionJournalsQuery(
    Guid OrganizationId,
    ProductionJournalStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<ProductionJournalListItemDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.ProductionJournalView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record ProductionJournalListItemDto(
    Guid Id, string Code, DateOnly Date, string? Reference, Guid ProductId, string ProductName,
    decimal OutputQuantity, decimal? FinishedGoodsCost, ProductionJournalStatus Status);
