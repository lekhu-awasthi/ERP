using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Imports.Commands.CreateImportJob;
using ErpApp.Domain.Imports;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports.Queries.GetImportJob;

/// <summary>
/// One job plus a page of its row outcomes -- the screen's progress poll and its results grid in a
/// single round trip.
///
/// <para><see cref="FailedRowsOnly"/> defaults to true because that is what the screen actually
/// needs: on a 5,000-row import the interesting rows are the handful that were rejected, and paging
/// through 4,997 successes to find them would be the wrong default. The counts on the job carry the
/// successes, and they are computed server-side over the full set, never summed from a page.</para>
/// </summary>
public sealed record GetImportJobQuery(
    Guid OrganizationId,
    Guid Id,
    bool FailedRowsOnly = true,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<ImportJobDetail>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ImportJobView;
}

public sealed record ImportJobDetail(ImportJobSummary Job, PagedResult<ImportJobRowDto> Rows);

public sealed record ImportJobRowDto(
    int RowNumber,
    ImportJobRowStatus Status,
    string? ColumnName,
    string? Message,
    Guid? TargetId,
    string? TargetCode);

public sealed class GetImportJobQueryValidator : AbstractValidator<GetImportJobQuery>
{
    public GetImportJobQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        PagingValidation.ValidatePaging(this, x => x.Page, x => x.PageSize);
    }
}

public sealed class GetImportJobQueryHandler(IAppDbContext db)
    : IRequestHandler<GetImportJobQuery, ImportJobDetail>
{
    public async Task<ImportJobDetail> Handle(GetImportJobQuery request, CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Import job not found.");

        var initiatedByName = await db.Users
            .Where(u => u.Id == job.InitiatedByUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var rowsQuery = db.ImportJobRows
            .AsNoTracking()
            .Where(r => r.ImportJobId == job.Id && r.OrganizationId == request.OrganizationId);

        if (request.FailedRowsOnly)
        {
            rowsQuery = rowsQuery.Where(r => r.Status == ImportJobRowStatus.Failed);
        }

        var totalCount = await rowsQuery.CountAsync(cancellationToken);

        var rows = await rowsQuery
            .OrderBy(r => r.RowNumber)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new ImportJobRowDto(r.RowNumber, r.Status, r.ColumnName, r.Message, r.TargetId, r.TargetCode))
            .ToListAsync(cancellationToken);

        return new ImportJobDetail(
            ImportJobMapper.ToSummary(job, initiatedByName),
            new PagedResult<ImportJobRowDto>(rows, request.Page, request.PageSize, totalCount));
    }
}
