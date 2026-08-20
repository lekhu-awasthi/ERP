using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Queries.SystemAuditReport;

public sealed class SystemAuditReportQueryHandler(IAppDbContext db)
    : IRequestHandler<SystemAuditReportQuery, PagedResult<AuditRowDto>>
{
    public async Task<PagedResult<AuditRowDto>> Handle(
        SystemAuditReportQuery request, CancellationToken cancellationToken)
    {
        var query = db.Audits.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.UserId is { } userId)
        {
            query = query.Where(x => x.UserId == userId);
        }

        if (request.Action is { } action)
        {
            query = query.Where(x => x.Action == action);
        }

        if (request.DocumentType is { } documentType)
        {
            query = query.Where(x => x.DocumentType == documentType);
        }

        // Business-day filters against CreatedAt (a system timestamp, not a document Date field
        // like every other report) -- bounds are built as explicit UTC instants rather than
        // comparing DateOnly to DateTimeOffset directly, which EF Core can't translate.
        if (request.FromDate is { } fromDate)
        {
            var fromUtc = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(x => x.CreatedAt >= fromUtc);
        }

        if (request.ToDate is { } toDate)
        {
            var toExclusiveUtc = new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(x => x.CreatedAt < toExclusiveUtc);
        }

        var entries = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.Id, x.UserId, x.Action, x.DocumentType, x.DocumentId, x.CreatedAt })
            .ToListAsync(cancellationToken);

        var userIds = entries.Select(x => x.UserId).Distinct().ToList();
        var userNames = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        // Payment rows need their Direction to pick the right of two Angular detail routes (see
        // AuditRowDto's own doc comment) -- looked up here rather than stored on Audit itself,
        // since Audit stays generic across every document type (architecture-spec.md §3.9's
        // future Activity-tab reuse).
        var paymentIds = entries.Where(x => x.DocumentType == DocumentType.Payment)
            .Select(x => x.DocumentId).Distinct().ToList();
        var paymentDirections = await db.Payments
            .Where(x => paymentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Direction })
            .ToDictionaryAsync(x => x.Id, x => x.Direction, cancellationToken);

        var rows = entries
            .Select(x => new AuditRowDto(
                x.Id, x.CreatedAt, x.UserId, userNames.GetValueOrDefault(x.UserId, "—"), x.Action,
                x.DocumentType, x.DocumentId,
                x.DocumentType == DocumentType.Payment ? paymentDirections.GetValueOrDefault(x.DocumentId) : null))
            .ToList();

        return request.ExportAll
            ? rows.ToUnpagedResult()
            : rows.ToPagedResult(request.Page, request.PageSize);
    }
}
