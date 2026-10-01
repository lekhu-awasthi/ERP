using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.GetPosDaySummary;

/// <summary>A session that rang up sales on the day.</summary>
public sealed record PosDaySessionDto(
    Guid Id, string Code, Guid LocationId, Guid UserId, string UserName, PosSessionStatus Status, decimal? CashDifference);

/// <summary>
/// One business day's till takings, read from <see cref="PosSalesReader"/> -- the reader every
/// session total comes from, so the day and its sessions cannot disagree (the vendor's defect 7).
/// </summary>
public sealed record PosDaySummaryDto(
    DateOnly Date,
    Guid? LocationId,
    PosSalesSummaryDto Sales,
    IReadOnlyList<PosDaySessionDto> Sessions);

/// <summary>
/// Phase 61 -- the day's figures behind phase 66's Day Report. Admin-only through
/// <see cref="PermissionKeys.PosSessionViewAll"/>: it sums every cashier's drawer.
/// </summary>
public sealed record GetPosDaySummaryQuery(Guid OrganizationId, DateOnly Date, Guid? LocationId = null)
    : IRequest<PosDaySummaryDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSessionViewAll;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}
