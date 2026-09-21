using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.ListBookTransactions;

public sealed class ListBookTransactionsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListBookTransactionsQuery, PagedResult<BookTransactionDto>>
{
    public async Task<PagedResult<BookTransactionDto>> Handle(
        ListBookTransactionsQuery request, CancellationToken cancellationToken)
    {
        var accountExists = await db.Accounts.AnyAsync(
            x => x.Id == request.BankAccountId && x.OrganizationId == request.OrganizationId,
            cancellationToken);

        if (!accountExists)
        {
            throw new NotFoundException("Bank account not found.");
        }

        var reader = new BankBookTransactionReader(db);

        var filter = new BookMovementFilter(
            request.OrganizationId,
            request.BankAccountId,
            request.Reconciled,
            request.FromDate,
            request.ToDate);

        var total = await reader.CountAsync(filter, cancellationToken);

        // A count of zero is a complete answer -- never issue the page query after one (phase 42).
        if (total == 0)
        {
            return new PagedResult<BookTransactionDto>([], request.Page, request.PageSize, 0);
        }

        var page = await reader.PageAsync(filter, request.Page, request.PageSize, cancellationToken);

        var rows = await reader.DescribeAsync(
            request.OrganizationId, request.BankAccountId, page, cancellationToken);

        return new PagedResult<BookTransactionDto>(rows, request.Page, request.PageSize, total);
    }
}
