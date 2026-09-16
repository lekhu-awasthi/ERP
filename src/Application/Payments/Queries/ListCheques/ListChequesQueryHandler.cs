using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Queries.ListCheques;

public sealed class ListChequesQueryHandler(IAppDbContext db) : IRequestHandler<ListChequesQuery, PagedResult<ChequeDto>>
{
    public async Task<PagedResult<ChequeDto>> Handle(ListChequesQuery request, CancellationToken cancellationToken)
    {
        // Phase 50 -- the tenant predicate stays on the Cheque ALONE, and that is a measured refusal
        // rather than an oversight, so it is recorded where somebody would otherwise "fix" it.
        //
        // Stating OrganizationId on the other three joins is redundant as a filter (a cheque's
        // payment, contact and account are all its own tenant's) but not as a plan, and this phase
        // added it for a good reason: the search below matches `contact.Name`, so the optimizer must
        // form the joined row before it can evaluate the term, and with the predicate here alone it
        // reads every tenant's Contacts and Payments to do it. It worked -- a non-matching term went
        // from 591,494 logical reads to 325,357.
        //
        // Then the paths it was NOT for were re-measured, against the shipped configuration below:
        // the Received tab went from 2,090 logical reads to 83,734, the status tab from 1,327 to
        // 67,774, and the first page from 29.5 ms of CPU to 674.9. Four leading-OrganizationId
        // indexes gave the optimizer four things to drive from and it chose wrong on every path but
        // the one the change was for. Phase 34c's rule, against this phase's own idea.
        //
        // tools/scale/probe-cheque-p50-D-tenant-predicate.csv is that measurement, kept so the
        // refusal can be checked rather than believed.
        var query =
            from cheque in db.Cheques
            join payment in db.Payments on cheque.LinkedPaymentId equals payment.Id
            join contact in db.Contacts on payment.ContactId equals contact.Id
            join account in db.Accounts on cheque.AccountId equals account.Id
            where cheque.OrganizationId == request.OrganizationId
            select new { cheque, payment, contact, account };

        if (request.Direction is { } direction)
        {
            query = query.Where(x => x.cheque.Direction == direction);
        }

        if (request.Status is { } status)
        {
            query = query.Where(x => x.cheque.Status == status);
        }

        if (request.ContactId is { } contactId)
        {
            query = query.Where(x => x.payment.ContactId == contactId);
        }

        if (request.FromDate is { } fromDate)
        {
            query = query.Where(x => x.cheque.ChequeDate >= fromDate);
        }

        if (request.ToDate is { } toDate)
        {
            query = query.Where(x => x.cheque.ChequeDate <= toDate);
        }

        // Phase 34b (NFR-6.1) -- the list search. This query already had FromDate/ToDate before the
        // phase, which is why it satisfies IDateRangeFilteredQuery without new parameters; the two
        // are the same filter the shell's global range now drives.
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.cheque.ChequeNo.Contains(term) || x.contact.Name.Contains(term));
        }

        // Phase 50 -- the seventeenth list, and the reason it was still the only one paging by
        // offset. Phase 42 swept "every paginated document list" onto ToKeyPagedResultAsync and got
        // sixteen; this register is not a document list, so it was invisible to that sweep in exactly
        // the way it was invisible to phase 34c's index convention and phase 47's sort sweep. Three
        // sweeps, one blind spot, and the screen is the same one each time.
        //
        // Measured on the 50,000-cheque tenant, last page (offset 49,950): 4,230 logical reads and
        // 697.6 ms of CPU fetching whole joined rows, against 1,289 and 40.9 ms paging the keys
        // first. Phase 42's finding, on the table phase 42 never saw.
        //
        // It also carries the OTHER half of that helper, which turned out to matter more here than
        // the tail did. A search term matching nothing now costs ONE statement: the count answers
        // the question and the page query is never issued ("a count of zero is a complete answer").
        // That is what makes the (OrganizationId, ChequeDate) index shippable -- on its own the
        // index took a non-matching term from 430,084 logical reads to 590,603, the regression phase
        // 34c warns an index always risks on the paths it was not added for, and this takes the same
        // term to 3,957. The register's search cost is now concentrated entirely in a term that
        // MATCHES, where the OR across the join to Contacts.Name has to form the joined row before
        // it can be evaluated; see docs/phase-50-status.md carried item #1.
        var paged = await query.ToKeyPagedResultAsync(
            x => x.cheque.Id,
            source => source.OrderByDescending(x => x.cheque.ChequeDate),
            request.Page,
            request.PageSize,
            cancellationToken);

        // Projected after the page is materialised, never before it: a record built store-side is
        // the shape phase 42 found untranslatable, and fifty rows in memory is not a cost.
        return new PagedResult<ChequeDto>(
            [.. paged.Items.Select(x => new ChequeDto(
                x.cheque.Id, x.cheque.LinkedPaymentId, x.cheque.Direction, x.payment.ContactId, x.contact.Name,
                x.cheque.AccountId, x.account.Name, x.cheque.ChequeNo, x.cheque.ChequeDate, x.cheque.ReceivedDate,
                x.cheque.Amount, x.cheque.Status))],
            paged.Page,
            paged.PageSize,
            paged.TotalCount);
    }
}
