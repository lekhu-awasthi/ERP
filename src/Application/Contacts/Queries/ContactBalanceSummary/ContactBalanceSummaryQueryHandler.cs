using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.ContactBalanceSummary;

/// <summary>
/// Closing balance per contact = <c>Contact.OpeningBalance</c> plus every ledger event up to
/// <c>ToDate</c> -- read through <see cref="ContactLedgerReader"/>, the same code
/// <c>ContactStatementQueryHandler</c> uses, so this report's Closing Balance column and the
/// Statement's Closing Balance row are the same number by construction rather than by agreement
/// between two implementations.
///
/// <para><b>Contacts with no activity still appear if they carry an opening balance</b>, and
/// contacts whose balance nets to exactly zero do not appear at all -- matching the live report,
/// which returned 78 customer rows where the same period's Invoice Age returned 16 documents, and
/// which showed contacts holding nothing but a credit.</para>
///
/// <para><b>Only the ContactGroup filter narrows the row set.</b> There is no "as of" cut on the
/// contact list itself: a contact is in this report if its balance is non-zero on
/// <c>ToDate</c>.</para>
/// </summary>
public sealed class ContactBalanceSummaryQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ContactBalanceSummaryQuery, ContactBalanceSummaryDto>
{
    public async Task<ContactBalanceSummaryDto> Handle(ContactBalanceSummaryQuery request, CancellationToken cancellationToken)
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

        if (request.ContactGroupId is { } groupId)
        {
            contactsQuery = contactsQuery.Where(x => x.GroupId == groupId);
        }

        var contacts = await contactsQuery
            .Select(x => new { x.Id, x.Code, x.Name, x.GroupId, x.OpeningBalance })
            .ToListAsync(cancellationToken);

        var groupIds = contacts.Where(x => x.GroupId != null).Select(x => x.GroupId!.Value).Distinct().ToList();
        var groupNames = await db.ContactGroups
            .Where(x => groupIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var events = await ContactLedgerReader.LoadAllContactEventsAsync(
            db, request.OrganizationId, request.ContactType, request.ToDate, cancellationToken,
            request.LocationId, reportLocations);

        var movementByContact = events
            .GroupBy(x => x.ContactId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.SignedAmount));

        var rows = contacts
            .Select(c => new
            {
                Contact = c,
                // Phase 35b -- see ContactStatementQueryHandler: OpeningBalance carries no location,
                // so a filtered balance is the carry-forward plus this branch's movements.
                Balance = c.OpeningBalance + movementByContact.GetValueOrDefault(c.Id),
            })
            .Where(x => x.Balance != 0)
            .Select(x => new ContactBalanceSummaryRowDto(
                x.Contact.Id,
                x.Contact.Code,
                x.Contact.Name,
                x.Contact.GroupId is { } gid ? groupNames.GetValueOrDefault(gid) : null,
                x.Balance,
                ContactLedgerReader.BalanceType(request.ContactType, x.Balance)))
            .OrderBy(x => x.ContactCode)
            .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);
        var total = rows.Sum(x => x.ClosingBalance);

        return new ContactBalanceSummaryDto(
            request.ContactType,
            request.FromDate,
            request.ToDate,
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount,
            total,
            ContactLedgerReader.BalanceType(request.ContactType, total));
    }
}
