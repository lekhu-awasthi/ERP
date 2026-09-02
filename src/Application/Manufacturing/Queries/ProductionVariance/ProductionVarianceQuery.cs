using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.ProductionVariance;

/// <summary>
/// Reports &gt; Inventory Report &gt; Production Variance Report. Read live on 2026-09-02: it
/// compares, per input and by-product line, the Voucher Quantity actually used against the BOM
/// Quantity, and shows Variance Quantity and Variance %. Only journals that carry a BOM appear at
/// all -- there is nothing to vary against otherwise.
///
/// <para><b>One deliberate correction to what was observed.</b> The reference report's BOM Quantity
/// appears not to be scaled to the journal's own output: a run of 10 against a BOM whose output is
/// 12 and whose raw material is 12 (a 1:1 ratio) reported a BOM Quantity of 12.5 and a 36%
/// variance, which compares a plan for one batch size against a run of another. Here the BOM
/// quantity is scaled by (journal output / BOM output) first, so the same run reports a plan of 10
/// and a variance of 2 against an actual of 8. Anything else labels a correctly-sized run as
/// variant purely because the batch sizes differ.</para>
/// </summary>
public sealed record ProductionVarianceQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ProductId,
    Guid? CategoryId,
    bool ExportAll = false,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<ProductionVarianceRowDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.ProductionReportView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

/// <summary><see cref="VariancePct"/> is null when the plan is zero -- a line the BOM never
/// mentioned at all. Reporting an infinite or 100% variance there would be arithmetic dressed up
/// as information.</summary>
public sealed record ProductionVarianceLineDto(
    Guid ProductId,
    string ProductName,
    string ProductCode,
    string? UnitName,
    bool IsByProduct,
    decimal VoucherQuantity,
    decimal BomQuantity,
    decimal VarianceQuantity,
    decimal? VariancePct);

public sealed record ProductionVarianceRowDto(
    Guid Id,
    DateOnly Date,
    string Code,
    string? Reference,
    Guid ProductId,
    string ProductName,
    decimal QuantityProduced,
    IReadOnlyList<ProductionVarianceLineDto> Lines);
