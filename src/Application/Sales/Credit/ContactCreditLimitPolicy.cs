using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Credit;

/// <summary>
/// Phase 31's implementation of <see cref="ICreditLimitPolicy"/>.
///
/// <para><b>It reads the balance through <c>ContactLedgerReader</c>, not through the general
/// ledger.</b> That is phase-26b's shared-reader discipline applied here: the number this check
/// compares against is by construction the same number the Contact Overview widget and the Customer
/// Statement show, so a user who is told "26,800 exceeds your limit" can open the contact and find
/// 26,800. Deriving it independently from <c>GlLine</c> would have produced a second answer to the
/// same question, which is exactly what that discipline exists to prevent.</para>
///
/// <para><b>Customers only.</b> The tenant setting's own wording is "when a <i>Customer's</i>
/// balance is about to exceed it's credit limit", and the only caller is Invoice Approve. A supplier
/// carries a CreditLimit because the live form offers one, but nothing reads it -- see
/// docs/phase-31-status.md Decision B. A non-Customer contact returns Ok here rather than throwing,
/// so a mis-typed document fails on its own terms rather than on this one.</para>
///
/// <para><b>Currency (phase 36).</b> Everything compared here is in the base currency:
/// <c>ContactLedgerReader</c> folds each event at its own document's rate, <c>Contact.CreditLimit</c>
/// and <c>Contact.OpeningBalance</c> are base-currency figures already, and the caller folds the new
/// document's total before passing it in. Until phase 36 the ledger summed mixed currencies as
/// though they were one unit and this check compared that sum against a base-currency limit -- phase
/// 31 recorded it as a carried limitation inherited from phase 28. A single-currency tenant sees no
/// change: every rate is 1.</para>
/// </summary>
public sealed class ContactCreditLimitPolicy(IAppDbContext db) : ICreditLimitPolicy
{
    public async Task<CreditLimitCheckResult> CheckAsync(
        Guid organizationId, Guid contactId, decimal documentAmount, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == contactId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Contact not found.");

        // Zero is "no limit" (Contact.CreditLimit), so there is nothing to check and no ledger to
        // load -- which also means this whole check costs a fresh tenant one already-loaded row.
        if (contact.CreditLimit <= 0m || contact.Type != ContactType.Customer)
        {
            return new CreditLimitCheckResult(
                CreditLimitStatus.Ok, contact.Name, contact.Code, 0m, contact.CreditLimit);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = await ContactLedgerReader.LoadEventsAsync(
            db, organizationId, ContactType.Customer, contactId, today, cancellationToken);

        var projectedBalance = contact.OpeningBalance + events.Sum(x => x.SignedAmount) + documentAmount;

        if (projectedBalance <= contact.CreditLimit)
        {
            return new CreditLimitCheckResult(
                CreditLimitStatus.Ok, contact.Name, contact.Code, projectedBalance, contact.CreditLimit);
        }

        var settings = await db.TenantSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        var status = settings.CreditLimitExceedsAction switch
        {
            BalanceAction.Reject => CreditLimitStatus.Reject,
            BalanceAction.DoNothing => CreditLimitStatus.Ok,
            _ => CreditLimitStatus.Warn,
        };

        return new CreditLimitCheckResult(
            status, contact.Name, contact.Code, projectedBalance, contact.CreditLimit);
    }
}
