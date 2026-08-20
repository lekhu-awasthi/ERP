using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.ListBankAccounts;

/// <summary>
/// Balance computation mirrors TrialBalanceQueryHandler's own GlLines sum (Debit - Credit) grouped
/// by AccountId -- same pattern, just with no AsOfDate cutoff (a Bank Account's card shows its
/// live, as-of-now balance). Accounts are paginated at the SQL level first (no footer/grand-total
/// on this screen, so there's no phase-16c-style need to materialize the full filtered set before
/// paging); GL sums are then batch-fetched only for the current page's AccountIds.
/// </summary>
public sealed class ListBankAccountsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListBankAccountsQuery, PagedResult<BankAccountDto>>
{
    public async Task<PagedResult<BankAccountDto>> Handle(ListBankAccountsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Accounts.Where(
            x => x.OrganizationId == request.OrganizationId
                && (x.Kind == AccountKind.Bank || x.Kind == AccountKind.Cash)
                && x.IsActive == request.IsActive);

        var page = await query.OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name, x.Kind, x.BankId, x.AccountNumber, x.IsActive })
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);

        var accountIds = page.Items.Select(x => x.Id).ToList();

        var balances = await (
                from line in db.GlLines
                join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
                where entry.OrganizationId == request.OrganizationId && accountIds.Contains(line.AccountId)
                group line by line.AccountId into g
                select new { AccountId = g.Key, Balance = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Balance, cancellationToken);

        var bankIds = page.Items.Where(x => x.BankId.HasValue).Select(x => x.BankId!.Value).Distinct().ToList();
        var bankNames = await db.Banks.Where(x => bankIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var items = page.Items.Select(x => new BankAccountDto(
                x.Id, x.Code, x.Name, x.Kind.ToString(), x.BankId,
                x.BankId.HasValue ? bankNames.GetValueOrDefault(x.BankId.Value) : null,
                x.AccountNumber, x.IsActive, balances.GetValueOrDefault(x.Id, 0m)))
            .ToList();

        return new PagedResult<BankAccountDto>(items, page.Page, page.PageSize, page.TotalCount);
    }
}
