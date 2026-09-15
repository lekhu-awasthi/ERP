using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Queries.SystemAuditReport;

public sealed class SystemAuditReportQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SystemAuditReportQuery, PagedResult<AuditRowDto>>
{
    public async Task<PagedResult<AuditRowDto>> Handle(
        SystemAuditReportQuery request, CancellationToken cancellationToken)
    {
        // Phase 44 -- TenantSettings.LocationWiseReportPermission, the second narrowing every other
        // location-filtered report has applied since phase 35b. Null (unrestricted) unless the tenant
        // has turned the toggle on AND this caller's role carries location-specific grants, so no
        // existing tenant's figures change.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        var query = db.Audits.Where(x => x.OrganizationId == request.OrganizationId);

        // Composed as its own Where, never folded into a `scope == null || scope.Contains(...)` --
        // an expression tree does not short-circuit, so the folded form hands EF a null list to
        // translate on the unrestricted branch, which is almost every caller (known-gotchas).
        //
        // A row with no location is hidden from this narrowing, the same way ReportLocationFilter
        // hides one: it matches AuthorizationBehavior's own LocationScopeOutcome.NoLocation branch --
        // there is nothing a location grant can cover.
        if (reportLocations is not null)
        {
            query = query.Where(x => x.LocationId != null && reportLocations.Contains(x.LocationId.Value));
        }

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

        // Phase 44 (35b carried item #2) -- the Billing Location filter, over the column
        // AuditBehavior now stamps. Applied to the audit row's own LocationId rather than by joining
        // back to the document, which is the rule for an append-only fact (phase 35b).
        if (request.LocationId is { } locationId)
        {
            query = query.Where(x => x.LocationId == locationId);
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
            .Select(x => new { x.Id, x.UserId, x.Action, x.DocumentType, x.DocumentId, x.CreatedAt, x.LocationId })
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

        // Phase 44 -- location names for the stamped ids. Loaded for the tenant rather than for
        // the ids this page mentions: the list is a handful of rows, and phase 42's measurement was
        // that sending a list of ids back to SQL Server costs more than the scan it avoids.
        var locationNames = await db.BillingLocations
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var rows = entries
            .Select(x => new AuditRowDto(
                x.Id, x.CreatedAt, x.UserId, userNames.GetValueOrDefault(x.UserId, "—"), x.Action,
                x.DocumentType, x.DocumentId,
                x.DocumentType == DocumentType.Payment ? paymentDirections.GetValueOrDefault(x.DocumentId) : null,
                x.LocationId is { } locationId ? locationNames.GetValueOrDefault(locationId) : null))
            .ToList();

        return request.ExportAll
            ? rows.ToUnpagedResult()
            : rows.ToPagedResult(request.Page, request.PageSize);
    }
}
