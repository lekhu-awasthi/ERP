using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.ListOpeningBalanceLines;

public sealed class ListAccountOpeningBalancesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListAccountOpeningBalancesQuery, PagedResult<AccountOpeningBalanceDto>>
{
    public async Task<PagedResult<AccountOpeningBalanceDto>> Handle(
        ListAccountOpeningBalancesQuery request, CancellationToken cancellationToken)
    {
        var query =
            from account in db.Accounts
            join group_ in db.AccountGroups on account.GroupId equals group_.Id
            join line in db.OpeningBalanceLines.Where(x => x.OrganizationId == request.OrganizationId)
                on account.Id equals line.AccountId into lines
            from line in lines.DefaultIfEmpty()
            where account.OrganizationId == request.OrganizationId
            orderby account.Code
            select new AccountOpeningBalanceDto(
                account.Id, account.Code, account.Name, account.RootType.ToString(), group_.Name,
                line == null ? 0m : line.Debit, line == null ? 0m : line.Credit,
                line == null ? (Guid?)null : line.Id);

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
