using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Queries.ListCheques;

public sealed class ListChequesQueryHandler(IAppDbContext db) : IRequestHandler<ListChequesQuery, PagedResult<ChequeDto>>
{
    public async Task<PagedResult<ChequeDto>> Handle(ListChequesQuery request, CancellationToken cancellationToken)
    {
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

        return await query
            .OrderByDescending(x => x.cheque.ChequeDate)
            .Select(x => new ChequeDto(
                x.cheque.Id, x.cheque.LinkedPaymentId, x.cheque.Direction, x.payment.ContactId, x.contact.Name,
                x.cheque.AccountId, x.account.Name, x.cheque.ChequeNo, x.cheque.ChequeDate, x.cheque.ReceivedDate,
                x.cheque.Amount, x.cheque.Status))
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
