using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.ListOpeningBalanceLines;

public sealed class ListAccountOpeningBalancesQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListAccountOpeningBalancesQuery, PagedResult<AccountOpeningBalanceDto>>
{
    public async Task<PagedResult<AccountOpeningBalanceDto>> Handle(
        ListAccountOpeningBalancesQuery request, CancellationToken cancellationToken)
    {

        // Phase 32b. These two lists enumerate MASTER records (accounts, products) with the
        // opening line LEFT-joined on, so a location restriction narrows the joined figures rather
        // than hiding rows: a caller scoped to one branch still sees the whole chart of accounts,
        // and sees opening balances only for their own locations. Filtering the outer row instead
        // would hide accounts that merely happen to have no opening balance at that location.
        var allowedLocations = await LocationAccessScope.ForKeyAsync(
            db, currentUser, request.OrganizationId, request.PermissionKey, cancellationToken);

        var query =
            from account in db.Accounts
            join group_ in db.AccountGroups on account.GroupId equals group_.Id
            join line in db.OpeningBalanceLines.Where(
                    x => x.OrganizationId == request.OrganizationId
                         && (allowedLocations == null
                             || (x.LocationId != null && allowedLocations.Contains(x.LocationId.Value))))
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
