using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Imports.Commands.CreateImportJob;
using ErpApp.Domain.Imports;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports.Queries.ListImportJobs;

/// <summary>The Import / Export screen's history grid, newest first. Paginated server-side from the
/// start (NFR-5.1) -- see phase-16c-status.md's bug #1 for why a page's worth of rows must never be
/// summed client-side.</summary>
/// <param name="EntityTypes">
/// Restricts the history to these upload types; null or empty means all of them. Added in Phase 21c
/// so the Migration screen and the Import / Export screen can share one endpoint and one job table
/// while each showing only its own history -- the two are separate screens in the reference product,
/// and a migrated tax-register upload appearing in the master-data import log (or vice versa) would
/// be exactly the cross-reading Decision B set out to avoid.
/// </param>
public sealed record ListImportJobsQuery(
    Guid OrganizationId,
    IReadOnlyList<ImportEntityType>? EntityTypes = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<ImportJobSummary>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ImportJobView;
}

public sealed class ListImportJobsQueryValidator : AbstractValidator<ListImportJobsQuery>
{
    public ListImportJobsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        PagingValidation.ValidatePaging(this, x => x.Page, x => x.PageSize);
    }
}

public sealed class ListImportJobsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListImportJobsQuery, PagedResult<ImportJobSummary>>
{
    public async Task<PagedResult<ImportJobSummary>> Handle(
        ListImportJobsQuery request, CancellationToken cancellationToken)
    {
        var entityTypes = request.EntityTypes is { Count: > 0 } ? request.EntityTypes : null;

        var query =
            from job in db.ImportJobs
            where job.OrganizationId == request.OrganizationId
                && (entityTypes == null || entityTypes.Contains(job.EntityType))
            join user in db.Users on job.InitiatedByUserId equals user.Id into initiators
            from initiator in initiators.DefaultIfEmpty()
            orderby job.CreatedAt descending
            select new { Job = job, InitiatorName = initiator == null ? string.Empty : initiator.FullName };

        var totalCount = await query.CountAsync(cancellationToken);

        var page = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ImportJobSummary>(
            [.. page.Select(x => ImportJobMapper.ToSummary(x.Job, x.InitiatorName))],
            request.Page,
            request.PageSize,
            totalCount);
    }
}
