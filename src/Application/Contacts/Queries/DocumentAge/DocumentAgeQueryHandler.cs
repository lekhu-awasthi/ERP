using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Contacts.Queries.Ageing;
using ErpApp.Domain.Contacts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.DocumentAge;

/// <summary>
/// Per-document outstanding, aged. The outstanding figures themselves come from
/// <see cref="OutstandingDocumentReader"/> -- the one place this codebase decides what a contact
/// still owes, document by document -- so this report and phase 9's Ageing Summary are a listing
/// and a bucketing of the <b>same</b> rows rather than two implementations of the same arithmetic
/// (phase 36; before it, the two had drifted twice and been patched back into agreement twice).
///
/// <para>What is left here is the presentation this screen owns: the Txn Type filter, the
/// contact-group column, Overdue-vs-Current, the age in days, oldest-due-first ordering and the
/// footer totals over the whole filtered set rather than the page (phase 16c).</para>
/// </summary>
public sealed class DocumentAgeQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<DocumentAgeQuery, DocumentAgeDto>
{
    public async Task<DocumentAgeDto> Handle(DocumentAgeQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null (unrestricted) unless the tenant has turned
        // the toggle on AND this caller's role carries location-specific grants, so no existing
        // tenant's figures change. Narrows rows in addition to request.LocationId, which is the
        // user's own filter -- two mechanisms, two reasons, both applied.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        var contactsQuery = db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && x.Type == request.ContactType);

        if (request.ContactId is { } onlyContact)
        {
            contactsQuery = contactsQuery.Where(x => x.Id == onlyContact);
        }

        var contacts = await contactsQuery
            .Select(x => new { x.Id, x.Code, x.Name, x.GroupId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var groupIds = contacts.Values.Where(x => x.GroupId != null).Select(x => x.GroupId!.Value).Distinct().ToList();
        var groupNames = await db.ContactGroups
            .Where(x => groupIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var outstanding = await OutstandingDocumentReader.LoadAsync(
            db, request.OrganizationId, request.ContactType, request.AsOfDate, cancellationToken,
            request.ContactId, request.LocationId, reportLocations);

        var wanted = request.DocumentTypes is { Count: > 0 } ? request.DocumentTypes.ToHashSet() : null;

        var rows = new List<DocumentAgeRowDto>();

        foreach (var document in outstanding)
        {
            if (wanted is not null && !wanted.Contains(document.Type))
            {
                continue;
            }

            if (!contacts.TryGetValue(document.ContactId, out var contact))
            {
                continue; // filtered out by ContactId, or not a contact of this report's type
            }

            var overdueBy = request.AsOfDate.DayNumber - document.DueDate.DayNumber;

            rows.Add(new DocumentAgeRowDto(
                document.Type,
                document.Id,
                document.Date,
                document.DueDate,
                document.Number,
                document.Reference,
                contact.Id,
                contact.Code,
                contact.Name,
                contact.GroupId is { } gid ? groupNames.GetValueOrDefault(gid) : null,
                document.Amount,
                document.Paid,
                document.Balance,
                overdueBy > 0 ? DocumentAgeRowDto.Overdue : DocumentAgeRowDto.Current,
                Math.Max(0, overdueBy)));
        }

        // Oldest due first -- the order an ageing report is read in, and the live screen's own.
        var ordered = rows.OrderBy(x => x.DueDate).ThenBy(x => x.Number, StringComparer.Ordinal).ToList();
        var paged = request.ExportAll ? ordered.ToUnpagedResult() : ordered.ToPagedResult(request.Page, request.PageSize);

        return new DocumentAgeDto(
            request.ContactType,
            request.FromDate,
            request.AsOfDate,
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount,
            ordered.Sum(x => x.Amount),
            ordered.Sum(x => x.Paid),
            ordered.Sum(x => x.Balance));
    }
}
