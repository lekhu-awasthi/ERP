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

    /// <summary>Phase 17 -- a Bank-kind Account's optional BankId must resolve to a real Bank
    /// lookup row in the same Organization when supplied.</summary>
    public static async Task EnsureBankExistsAsync(
        IAppDbContext db, Guid organizationId, Guid? bankId, CancellationToken cancellationToken)
    {
        if (bankId is null)
        {
            return;
        }

        var exists = await db.Banks.AnyAsync(
            x => x.Id == bankId.Value && x.OrganizationId == organizationId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Bank not found.");
        }
    }

    /// <summary>Decision #2 (docs/phase-17-status.md) -- a JournalVoucherLine's optional ContactId
    /// can be either a Customer or a Supplier (a line can tag either side's control account), so
    /// this is a plain existence+org-scope check, not type-restricted like
    /// Payments.PaymentValidation.EnsureContactExistsAsync.</summary>
    public static async Task EnsureContactsExistAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<Guid?> contactIds, CancellationToken cancellationToken)
    {
        var distinctIds = contactIds.Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();

        if (distinctIds.Count == 0)
        {
            return;
        }

        var existingCount = await db.Contacts.CountAsync(
            x => x.OrganizationId == organizationId && distinctIds.Contains(x.Id), cancellationToken);

        if (existingCount != distinctIds.Count)
        {
            throw new NotFoundException("One or more contacts were not found.");
        }
    }
}
