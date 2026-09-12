using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Contacts.Queries.Ageing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.ContactAgeingSummary;

/// <summary>
/// The per-contact bucketing of exactly the rows Invoice Age / Purchase Bill Age lists: both read
/// <see cref="OutstandingDocumentReader"/>, which is the one place this codebase decides what a
/// contact still owes, document by document.
///
/// <para><b>Phase 36 made that structural.</b> This report and phase-26b's per-document one were
/// two implementations of the same netting, and they drifted twice -- over
/// JournalVoucher-sourced allocations and over due-date-versus-document-date bucketing -- before
/// phase 31 patched both by editing this handler to match the other. That left them agreeing by
/// coincidence and still disagreeing about <i>which documents are ageable at all</i>: this one saw
/// neither a contact-tagged Journal Voucher nor a contact's own opening balance. Now a bucket total
/// is a partition of the other report's rows by construction, and <c>AgeingReportsAgreeTests</c>
/// asserts it. <b>That is a correction with a blast radius, stated rather than slipped in</b>
/// (phase-26b Decision B's own words): a tenant that has tagged a Journal Voucher with a contact,
/// or set a contact opening balance, sees larger buckets here than before. A tenant that has done
/// neither sees no change at all.</para>
///
/// <para>What remains this report's own: the Contact Group filter, the four buckets, and the
/// column totals. A standalone CreditNote/DebitNote (no ReferrerId matching an in-scope bill)
/// reduces the Contact's real balance but is not bucketed -- it has no specific bill to attach an
/// age to. It still shows up in ContactStatementQuery's flat ledger, which needs no such
/// attribution. See phase-9-status.md's scope decision for the reasoning (this is the one place
/// Ageing and Statement's totals can legitimately diverge for a Contact with a standalone reversal
/// on file).</para>
/// </summary>
public sealed class ContactAgeingSummaryQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ContactAgeingSummaryQuery, ContactAgeingSummaryDto>
{
    public async Task<ContactAgeingSummaryDto> Handle(ContactAgeingSummaryQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null (unrestricted) unless the tenant has turned
        // the toggle on AND this caller's role carries location-specific grants, so no existing
        // tenant's figures change. Narrows rows in addition to request.LocationId, which is the
        // user's own filter -- two mechanisms, two reasons, both applied.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        var outstanding = await OutstandingDocumentReader.LoadAsync(
            db, request.OrganizationId, request.ContactType, request.AsOfDate, cancellationToken,
            contactId: null, locationId: request.LocationId, reportLocations: reportLocations);

        var contactIds = outstanding.Select(x => x.ContactId).Distinct().ToList();
        var contactsQuery = db.Contacts.Where(x => x.OrganizationId == request.OrganizationId && x.Type == request.ContactType);
        if (request.ContactGroupId is { } groupId)
        {
            contactsQuery = contactsQuery.Where(x => x.GroupId == groupId);
        }

        var contacts = await contactsQuery
            .Where(x => contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name, x.GroupId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var groupIds = contacts.Values.Where(x => x.GroupId != null).Select(x => x.GroupId!.Value).Distinct().ToList();
        var groupNames = await db.ContactGroups
            .Where(x => groupIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var buckets = new Dictionary<Guid, decimal[]>();

        foreach (var document in outstanding)
        {
            if (!contacts.ContainsKey(document.ContactId))
            {
                continue; // filtered out by ContactGroupId
            }

            // Aged from the DUE date, which is the document's own date wherever nothing stores one.
            var age = request.AsOfDate.DayNumber - document.DueDate.DayNumber;
            var bucketIndex = age <= 30 ? 0 : age <= 60 ? 1 : age <= 90 ? 2 : 3;

            if (!buckets.TryGetValue(document.ContactId, out var contactBuckets))
            {
                contactBuckets = new decimal[4];
                buckets[document.ContactId] = contactBuckets;
            }

            contactBuckets[bucketIndex] += document.Balance;
        }

        var rows = buckets
            .Select(kvp =>
            {
                var contact = contacts[kvp.Key];
                var groupName = contact.GroupId is { } gid ? groupNames.GetValueOrDefault(gid) : null;
                return new ContactAgeingSummaryRowDto(
                    contact.Id, contact.Code, contact.Name, groupName,
                    kvp.Value[0], kvp.Value[1], kvp.Value[2], kvp.Value[3]);
            })
            .OrderBy(x => x.ContactCode)
            .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        return new ContactAgeingSummaryDto(
            request.AsOfDate, request.ContactType, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            rows.Sum(r => r.Days1To30), rows.Sum(r => r.Days31To60), rows.Sum(r => r.Days61To90), rows.Sum(r => r.Days91Plus));
    }
}
