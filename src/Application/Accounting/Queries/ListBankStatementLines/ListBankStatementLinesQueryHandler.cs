using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.ListBankStatementLines;

public sealed class ListBankStatementLinesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListBankStatementLinesQuery, PagedResult<BankStatementLineListItem>>
{
    public async Task<PagedResult<BankStatementLineListItem>> Handle(
        ListBankStatementLinesQuery request, CancellationToken cancellationToken)
    {
        var accountExists = await db.Accounts.AnyAsync(
            x => x.Id == request.BankAccountId && x.OrganizationId == request.OrganizationId,
            cancellationToken);

        if (!accountExists)
        {
            throw new NotFoundException("Bank account not found.");
        }

        var query = db.BankStatementLines.Where(
            x => x.OrganizationId == request.OrganizationId && x.BankAccountId == request.BankAccountId);

        // A separate composed `.Where()`, never folded into one predicate with a null check: an
        // expression tree does not short-circuit, so `term == null || x.Description.Contains(term)`
        // hands EF a null to translate on the unrestricted branch (phase-33's gotcha).
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.Description != null && x.Description.Contains(term));
        }

        if (request.FromDate is { } fromDate)
        {
            query = query.Where(x => x.Date >= fromDate);
        }

        if (request.ToDate is { } toDate)
        {
            query = query.Where(x => x.Date <= toDate);
        }

        // The two orderings this table is indexed for, both built by TenantIndexConvention:
        // (OrganizationId, CreatedAt DESC) and (OrganizationId, Date). Anything else would sort the
        // whole filtered set, which the pager would hide (34c).
        //
        // The default is CreatedAt, matching every other list in this codebase, and it is the
        // better default here for a reason the others do not have: two uploads of overlapping
        // periods interleave under a date ordering and sit in two blocks under this one.
        IOrderedQueryable<BankStatementLine> Order(IQueryable<BankStatementLine> source) => request.Sort switch
        {
            ListSort.DocumentDate => source.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt),
            _ => source.OrderByDescending(x => x.CreatedAt),
        };

        var page = await query.ToKeyPagedResultAsync(
            x => x.Id, Order, request.Page, request.PageSize, cancellationToken);

        // Projected after paging, never inside the store query: a projection the store cannot
        // translate is evaluated in C# by InMemory and 500s on SQL Server, and only an E2E sees it
        // (phase 42). StatementAmount's accessors are exactly that kind of projection -- they are
        // properties on a value type the provider knows only as a converted decimal.
        return new PagedResult<BankStatementLineListItem>(
            [.. page.Items.Select(x => new BankStatementLineListItem(
                x.Id,
                x.Date,
                x.Description,
                x.Amount.DepositAmount,
                x.Amount.WithdrawalAmount,
                x.Amount.Signed,
                x.ImportJobId,
                x.CreatedAt))],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}
