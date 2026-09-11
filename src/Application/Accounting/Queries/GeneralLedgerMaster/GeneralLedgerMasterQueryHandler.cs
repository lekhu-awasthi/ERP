using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.GeneralLedgerMaster;

public sealed class GeneralLedgerMasterQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GeneralLedgerMasterQuery, PagedResult<GeneralLedgerMasterRowDto>>
{
    public async Task<PagedResult<GeneralLedgerMasterRowDto>> Handle(
        GeneralLedgerMasterQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null (unrestricted) unless the tenant has turned
        // the toggle on AND this caller's role carries location-specific grants, so no existing
        // tenant's figures change.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);
        var entries = GlEntryLocations.ForReport(db, request.OrganizationId, request.LocationId, reportLocations);

        var fromUtc = GlDateBoundary.StartOfDayUtc(request.FromDate);
        var toUtc = GlDateBoundary.EndOfDayUtc(request.ToDate);

        var query =
            from line in db.GlLines
            join entry in entries on line.GlJournalEntryId equals entry.Id
            where entry.PostedAt >= fromUtc && entry.PostedAt <= toUtc
            select new
            {
                entry.PostedAt,
                entry.SourceDocumentType,
                entry.SourceDocumentId,
                line.Id,
                line.AccountId,
                line.Debit,
                line.Credit,
            };

        if (request.DocumentType is { } documentType)
        {
            query = query.Where(x => x.SourceDocumentType == documentType);
        }

        var lines = await query.ToListAsync(cancellationToken);

        // Newest first, the way the live report reads. GlLine.Id makes the order total so paging
        // can never show or skip the same line twice.
        var ordered = lines
            .OrderByDescending(x => x.PostedAt)
            .ThenBy(x => x.SourceDocumentId)
            .ThenBy(x => x.Id)
            .ToList();

        var paged = request.ExportAll ? ordered.ToUnpagedResult() : ordered.ToPagedResult(request.Page, request.PageSize);

        // Account facts and source documents are resolved for the returned page only -- the same
        // two-pass shape RecentTransactionsQueryHandler established.
        var classification = await GlAccountClassification.LoadAsync(db, request.OrganizationId, cancellationToken);
        var documentKeys = paged.Items
            .Select(x => (x.SourceDocumentType, x.SourceDocumentId))
            .Distinct()
            .ToList();
        var documents = await GlSourceDocumentResolver.LoadAsync(
            db, request.OrganizationId, documentKeys, cancellationToken);

        var rows = paged.Items.Select(x =>
        {
            var account = classification.For(x.AccountId);
            var document = documents.For(x.SourceDocumentType, x.SourceDocumentId);
            return new GeneralLedgerMasterRowDto(
                DateOnly.FromDateTime(x.PostedAt.UtcDateTime),
                x.SourceDocumentType,
                x.SourceDocumentId,
                document?.Code,
                document?.Reference,
                x.AccountId,
                account?.AccountCode ?? string.Empty,
                account?.AccountName ?? string.Empty,
                account?.ParentGroupName ?? string.Empty,
                account?.GroupTypeName ?? string.Empty,
                account?.RootType ?? default,
                x.Debit,
                x.Credit,
                document?.Direction);
        }).ToList();

        return new PagedResult<GeneralLedgerMasterRowDto>(rows, paged.Page, paged.PageSize, paged.TotalCount);
    }
}
