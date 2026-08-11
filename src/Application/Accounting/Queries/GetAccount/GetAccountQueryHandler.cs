using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.GetAccount;

public sealed class GetAccountQueryHandler(IAppDbContext db) : IRequestHandler<GetAccountQuery, Account>
{
    public async Task<Account> Handle(GetAccountQuery request, CancellationToken cancellationToken)
    {
        return await db.Accounts.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Account not found.");
    }
}
