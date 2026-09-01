using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Exports.Commands.CreateExportJob;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Queries.ListExportJobs;

/// <summary>The Import / Export screen's export history, newest first. Paginated server-side from
/// the start (NFR-5.1).</summary>
public sealed record ListExportJobsQuery(Guid OrganizationId, int Page = 1, int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<ExportJobSummary>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ExportJobView;
}

public sealed class ListExportJobsQueryValidator : AbstractValidator<ListExportJobsQuery>
{
    public ListExportJobsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        PagingValidation.ValidatePaging(this, x => x.Page, x => x.PageSize);
    }
}

public sealed class ListExportJobsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListExportJobsQuery, PagedResult<ExportJobSummary>>
{
    public async Task<PagedResult<ExportJobSummary>> Handle(
        ListExportJobsQuery request, CancellationToken cancellationToken)
    {
        var query =
            from job in db.ExportJobs
            where job.OrganizationId == request.OrganizationId
            join user in db.Users on job.InitiatedByUserId equals user.Id into initiators
            from initiator in initiators.DefaultIfEmpty()
            orderby job.CreatedAt descending
            select new { Job = job, InitiatorName = initiator == null ? string.Empty : initiator.FullName };

        var totalCount = await query.CountAsync(cancellationToken);

        var page = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        // ExportJobMapper deliberately does not project StorageKey onto the wire shape. The key is
        // an opaque IFileStorage identifier, but it is still the name of a file holding an entire
        // tenant, and nothing outside the server has any use for it -- the download endpoint
        // resolves it from the job id after its own permission check. See Decision F.
        return new PagedResult<ExportJobSummary>(
            [.. page.Select(x => ExportJobMapper.ToSummary(x.Job, x.InitiatorName))],
            request.Page,
            request.PageSize,
            totalCount);
    }
}
