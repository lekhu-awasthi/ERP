using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.ListCreditableInvoices;

public sealed class ListCreditableInvoicesQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListCreditableInvoicesQuery, PagedResult<CreditableInvoiceDto>>
{
    public async Task<PagedResult<CreditableInvoiceDto>> Handle(
        ListCreditableInvoicesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Invoices.Where(x => x.OrganizationId == request.OrganizationId
            && x.ContactId == request.ContactId
            && x.Status == InvoiceStatus.Approved
            && x.Channel == SalesChannel.Erp);

        // Phase 32b -- a caller granted the key only at certain billing locations sees those locations'
        // invoices; a note can only be raised at its invoice's location, so nothing else is choosable.
        var allowedLocations = await LocationAccessScope.ForKeyAsync(
            db, currentUser, request.OrganizationId, request.PermissionKey, cancellationToken);
        if (allowedLocations is not null)
        {
            query = query.Where(x => x.LocationId != null && allowedLocations.Contains(x.LocationId.Value));
        }

        // The form's own location: a note is raised where its invoice was.
        if (request.LocationId is { } locationId)
        {
            query = query.Where(x => x.LocationId == locationId);
        }

        // Composed, never folded into one predicate with a null check (phase 33's gotcha).
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.Code.Contains(term) || (x.Reference != null && x.Reference.Contains(term)));
        }

        var page = await query.ToKeyPagedResultAsync(
            x => x.Id,
            source => source.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt),
            request.Page, request.PageSize, cancellationToken);

        // A count of zero is a complete answer (phase 42).
        if (page.Items.Count == 0)
        {
            return new PagedResult<CreditableInvoiceDto>([], page.Page, page.PageSize, page.TotalCount);
        }

        var ids = page.Items.Select(x => x.Id).ToList();
        var excluded = request.ExcludingCreditNoteId ?? Guid.Empty;

        var invoiceLines = await db.InvoiceLines
            .Where(x => ids.Contains(x.InvoiceId))
            .Select(x => new { x.InvoiceId, x.Amount, x.ServiceChargeAmount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var totals = invoiceLines
            .GroupBy(x => x.InvoiceId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.ServiceChargeAmount + x.VatAmount));

        // What the notes against these invoices already credit, each way a note can name one. ERP invoices
        // carry no round-off and their notes none either (a till refund is against a till sale, which this
        // list leaves out), so lines are the whole of it -- the same figure CreditNoteInvoiceReferences
        // reads for the cap.
        var creditedLines = await (
            from line in db.CreditNoteLines
            join note in db.CreditNotes on line.CreditNoteId equals note.Id
            where note.OrganizationId == request.OrganizationId
                && note.Status != CreditNoteStatus.Void
                && note.Id != excluded
                && ((note.ReferrerType == DocumentType.Invoice && note.ReferrerId != null && ids.Contains(note.ReferrerId.Value))
                    || (note.AgainstInvoiceId != null && ids.Contains(note.AgainstInvoiceId.Value)))
            select new
            {
                note.ReferrerType,
                note.ReferrerId,
                note.AgainstInvoiceId,
                line.Amount,
                line.ServiceChargeAmount,
                line.VatAmount,
            })
            .ToListAsync(cancellationToken);
        var credited = creditedLines
            .GroupBy(x => x.ReferrerType == DocumentType.Invoice && x.ReferrerId is not null ? x.ReferrerId.Value : x.AgainstInvoiceId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.ServiceChargeAmount + x.VatAmount));

        var items = page.Items
            .Select(x =>
            {
                var total = totals.GetValueOrDefault(x.Id);
                var creditedTotal = credited.GetValueOrDefault(x.Id);
                return new CreditableInvoiceDto(
                    x.Id, x.Code, x.Date, x.LocationId, x.CurrencyCode, x.ExchangeRate, total, creditedTotal,
                    Math.Max(total - creditedTotal, 0));
            })
            .ToList();

        return new PagedResult<CreditableInvoiceDto>(items, page.Page, page.PageSize, page.TotalCount);
    }
}
