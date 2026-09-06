using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Cash;

/// <summary>
/// Reads each account's balance straight off <c>GlLine</c> (debits less credits, the Asset-normal
/// direction every Bank/Cash account sits on), which is the same figure the Trial Balance and the
/// Cash Flow Summary show for that account. Deriving it from Payment/CashTransfer rows instead
/// would have produced a second answer to a question the general ledger already answers --
/// phase-26b's shared-source discipline.
///
/// <para><b>Only Bank and Cash kinds are checked</b> (<see cref="AccountKind"/>). An outflow naming
/// an <c>Other</c>-kind account is dropped rather than checked: the setting is about "cash and bank
/// balance", and an ordinary asset account is allowed to go into credit for reasons that are none of
/// this policy's business.</para>
///
/// <para>The check is "as of now", not as of the document's date, deliberately: the reference
/// product's own wording is "when cash and bank balance is <i>about to be</i> negative", and a
/// back-dated document whose balance was fine at the time but is not now would otherwise pass while
/// leaving the account overdrawn today.</para>
/// </summary>
public sealed class GlCashBalancePolicy(IAppDbContext db) : ICashBalancePolicy
{
    public async Task<CashBalanceCheckResult> CheckAsync(
        Guid organizationId, IReadOnlyCollection<CashOutflow> outflows, CancellationToken cancellationToken)
    {
        var none = new CashBalanceCheckResult(CashBalanceStatus.Ok, string.Empty, string.Empty, 0m);

        var requested = outflows
            .Where(x => x.Amount > 0m)
            .GroupBy(x => x.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        if (requested.Count == 0)
        {
            return none;
        }

        var accountIds = requested.Keys.ToList();
        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && accountIds.Contains(x.Id)
                && (x.Kind == AccountKind.Bank || x.Kind == AccountKind.Cash))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToListAsync(cancellationToken);

        if (accounts.Count == 0)
        {
            return none;
        }

        var cashAccountIds = accounts.Select(x => x.Id).ToList();
        var balances = await db.GlLines
            .Where(x => cashAccountIds.Contains(x.AccountId))
            .GroupBy(x => x.AccountId)
            .Select(g => new { AccountId = g.Key, Balance = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Balance, cancellationToken);

        // Ordered by code so a document overdrawing two accounts names the same one every time --
        // an unordered "first offender" makes a message that changes between identical runs.
        var offender = accounts
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .Select(x => new { x.Code, x.Name, Projected = balances.GetValueOrDefault(x.Id) - requested[x.Id] })
            .FirstOrDefault(x => x.Projected < 0m);

        if (offender is null)
        {
            return none;
        }

        var settings = await db.TenantSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        var status = settings.NegativeCashBalanceAction switch
        {
            BalanceAction.Reject => CashBalanceStatus.Reject,
            BalanceAction.DoNothing => CashBalanceStatus.Ok,
            _ => CashBalanceStatus.Warn,
        };

        return new CashBalanceCheckResult(status, offender.Name, offender.Code, offender.Projected);
    }
}
