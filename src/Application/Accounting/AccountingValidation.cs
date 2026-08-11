using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting;

/// <summary>Shared existence check reused by every JournalVoucher/CashTransfer Create/Update
/// handler -- every line's AccountId (and CashTransfer's FromAccountId) must belong to the same
/// Organization.</summary>
internal static class AccountingValidation
{
    public static async Task EnsureAccountsExistAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<Guid> accountIds, CancellationToken cancellationToken)
    {
        var distinctIds = accountIds.Distinct().ToList();

        var existingCount = await db.Accounts.CountAsync(
            x => x.OrganizationId == organizationId && distinctIds.Contains(x.Id), cancellationToken);

        if (existingCount != distinctIds.Count)
        {
            throw new NotFoundException("One or more accounts were not found.");
        }
    }
}
